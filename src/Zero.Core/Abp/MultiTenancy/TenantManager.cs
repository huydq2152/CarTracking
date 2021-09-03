﻿using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Abp;
using Abp.Application.Features;
using Abp.Domain.Repositories;
using Abp.Domain.Uow;
using Abp.IdentityFramework;
using Abp.MultiTenancy;
using Zero.Authorization.Roles;
using Zero.Authorization.Users;
using Zero.Editions;
using Zero.MultiTenancy.Demo;
using Abp.Extensions;
using Abp.Notifications;
using Abp.Runtime.Security;
using Microsoft.AspNetCore.Identity;
using Zero.Notifications;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Abp.Authorization;
using Abp.Localization;
using Abp.Runtime.Session;
using Abp.UI;
using Zero.MultiTenancy.Payments;
using Castle.Core.Logging;
using Microsoft.EntityFrameworkCore;
using Zero.Customize;

namespace Zero.MultiTenancy
{
    /// <summary>
    /// Tenant manager.
    /// </summary>
    public class TenantManager : AbpTenantManager<Tenant, User>
    {
        public IAbpSession AbpSession { get; set; }

        private readonly IUnitOfWorkManager _unitOfWorkManager;
        private readonly RoleManager _roleManager;
        private readonly UserManager _userManager;
        private readonly IUserEmailer _userEmailer;
        private readonly TenantDemoDataBuilder _demoDataBuilder;
        private readonly INotificationSubscriptionManager _notificationSubscriptionManager;
        private readonly IAppNotifier _appNotifier;
        private readonly IAbpZeroDbMigrator _abpZeroDbMigrator;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IRepository<SubscribableEdition> _subscribableEditionRepository;
        private readonly ILogger _logger;
        private readonly IRepository<EditionPermission> _editionPermissionRepository;
        private readonly IRepository<Tenant> _tenantRepository;
        private readonly IRepository<EmailTemplate> _emailTemplateRepository;
        private readonly IPermissionManager _permissionManager;
        public TenantManager(
            IRepository<Tenant> tenantRepository,
            IRepository<TenantFeatureSetting, long> tenantFeatureRepository,
            EditionManager editionManager,
            IUnitOfWorkManager unitOfWorkManager,
            RoleManager roleManager,
            IUserEmailer userEmailer,
            TenantDemoDataBuilder demoDataBuilder,
            UserManager userManager,
            INotificationSubscriptionManager notificationSubscriptionManager,
            IAppNotifier appNotifier,
            IAbpZeroFeatureValueStore featureValueStore,
            IAbpZeroDbMigrator abpZeroDbMigrator,
            IPasswordHasher<User> passwordHasher,
            IRepository<SubscribableEdition> subscribableEditionRepository, 
            IRepository<EditionPermission> editionPermissionRepository, 
            IRepository<EmailTemplate> emailTemplateRepository, 
            IPermissionManager permissionManager) : base(
                tenantRepository,
                tenantFeatureRepository,
                editionManager,
                featureValueStore
            )
        {
            AbpSession = NullAbpSession.Instance;
            _tenantRepository = tenantRepository;
            _unitOfWorkManager = unitOfWorkManager;
            _roleManager = roleManager;
            _userEmailer = userEmailer;
            _demoDataBuilder = demoDataBuilder;
            _userManager = userManager;
            _notificationSubscriptionManager = notificationSubscriptionManager;
            _appNotifier = appNotifier;
            _abpZeroDbMigrator = abpZeroDbMigrator;
            _passwordHasher = passwordHasher;
            _subscribableEditionRepository = subscribableEditionRepository;
            _editionPermissionRepository = editionPermissionRepository;
            _emailTemplateRepository = emailTemplateRepository;
            _permissionManager = permissionManager;
            _logger = NullLogger.Instance;
        }

        public async Task<int> CreateWithAdminUserAsync(
            string tenancyName,
            string name,
            string adminPassword,
            string adminEmailAddress,
            string connectionString,
            bool isActive,
            int? editionId,
            bool shouldChangePasswordOnNextLogin,
            bool sendActivationEmail,
            DateTime? subscriptionEndDate,
            bool isInTrialPeriod,
            string emailActivationLink)
        {
            int newTenantId;
            long newAdminId;

            await CheckEditionAsync(editionId, isInTrialPeriod);

            if (isInTrialPeriod && !subscriptionEndDate.HasValue)
            {
                throw new UserFriendlyException(LocalizationManager.GetString(ZeroConsts.LocalizationSourceName, "TrialWithoutEndDateErrorMessage"));
            }

            using (var uow = _unitOfWorkManager.Begin(TransactionScopeOption.RequiresNew))
            {
                //Create tenant
                var tenant = new Tenant(tenancyName, name)
                {
                    IsActive = isActive,
                    EditionId = editionId,
                    SubscriptionEndDateUtc = subscriptionEndDate?.ToUniversalTime(),
                    IsInTrialPeriod = isInTrialPeriod,
                    ConnectionString = connectionString.IsNullOrWhiteSpace() ? null : SimpleStringCipher.Instance.Encrypt(connectionString)
                };

                await CreateAsync(tenant);
                await _unitOfWorkManager.Current.SaveChangesAsync(); //To get new tenant's id.

                //Create tenant database
                _abpZeroDbMigrator.CreateOrMigrateForTenant(tenant);

                //We are working entities of new tenant, so changing tenant filter
                using (_unitOfWorkManager.Current.SetTenantId(tenant.Id))
                {
                    //Create static roles for new tenant
                    CheckErrors(await _roleManager.CreateStaticRoles(tenant.Id));
                    await _unitOfWorkManager.Current.SaveChangesAsync(); //To get static role ids

                    //grant all permissions to admin role
                    var adminRole = _roleManager.Roles.Single(r => r.Name == StaticRoleNames.Tenants.Admin);
                    await _roleManager.GrantAllPermissionsAsync(adminRole);

                    //User role should be default
                    var userRole = _roleManager.Roles.Single(r => r.Name == StaticRoleNames.Tenants.User);
                    userRole.IsDefault = true;
                    CheckErrors(await _roleManager.UpdateAsync(userRole));

                    //Create admin user for the tenant
                    var adminUser = User.CreateTenantAdminUser(tenant.Id, adminEmailAddress);
                    adminUser.ShouldChangePasswordOnNextLogin = shouldChangePasswordOnNextLogin;
                    adminUser.IsActive = true;

                    if (adminPassword.IsNullOrEmpty())
                    {
                        adminPassword = await _userManager.CreateRandomPassword();
                    }
                    else
                    {
                        await _userManager.InitializeOptionsAsync(AbpSession.TenantId);
                        foreach (var validator in _userManager.PasswordValidators)
                        {
                            CheckErrors(await validator.ValidateAsync(_userManager, adminUser, adminPassword));
                        }

                    }

                    adminUser.Password = _passwordHasher.HashPassword(adminUser, adminPassword);

                    CheckErrors(await _userManager.CreateAsync(adminUser));
                    await _unitOfWorkManager.Current.SaveChangesAsync(); //To get admin user's id

                    //Assign admin user to admin role!
                    CheckErrors(await _userManager.AddToRoleAsync(adminUser, adminRole.Name));

                    //Notifications
                    await _appNotifier.WelcomeToTheApplicationAsync(adminUser);

                    //Send activation email
                    if (sendActivationEmail)
                    {
                        adminUser.SetNewEmailConfirmationCode();
                        try
                        {
                            await _userEmailer.SendEmailActivationLinkAsync(adminUser, emailActivationLink, adminPassword);
                        }
                        catch (Exception e)
                        {
                            _logger.Error($"Create Tenant - Send Active Email - Active Link : {emailActivationLink} - Admin Psw : {adminPassword}", e);
                        }
                        
                    }

                    await _unitOfWorkManager.Current.SaveChangesAsync();

                    await _demoDataBuilder.BuildForAsync(tenant);

                    newTenantId = tenant.Id;
                    newAdminId = adminUser.Id;
                }

                await uow.CompleteAsync();
            }

            //Used a second UOW since UOW above sets some permissions and _notificationSubscriptionManager.SubscribeToAllAvailableNotificationsAsync needs these permissions to be saved.
            using (var uow = _unitOfWorkManager.Begin(TransactionScopeOption.RequiresNew))
            {
                using (_unitOfWorkManager.Current.SetTenantId(newTenantId))
                {
                    await _notificationSubscriptionManager.SubscribeToAllAvailableNotificationsAsync(new UserIdentifier(newTenantId, newAdminId));
                    await _unitOfWorkManager.Current.SaveChangesAsync();
                    await uow.CompleteAsync();
                }
            }

            return newTenantId;
        }

        public async Task CheckEditionAsync(int? editionId, bool isInTrialPeriod)
        {
            if (!editionId.HasValue || !isInTrialPeriod)
            {
                return;
            }

            var edition = await _subscribableEditionRepository.GetAsync(editionId.Value);
            if (!edition.IsFree)
            {
                return;
            }

            var error = LocalizationManager.GetSource(ZeroConsts.LocalizationSourceName).GetString("FreeEditionsCannotHaveTrialVersions");
            throw new UserFriendlyException(error);
        }

        protected virtual void CheckErrors(IdentityResult identityResult)
        {
            identityResult.CheckErrors(LocalizationManager);
        }

        public decimal GetUpgradePrice(SubscribableEdition currentEdition, SubscribableEdition targetEdition, int totalRemainingHourCount, PaymentPeriodType paymentPeriodType)
        {
            int numberOfHoursPerDay = 24;

            var totalRemainingDayCount = totalRemainingHourCount / numberOfHoursPerDay;
            var unusedPeriodCount = totalRemainingDayCount / (int)paymentPeriodType;
            var unusedHoursCount = totalRemainingHourCount % ((int)paymentPeriodType * numberOfHoursPerDay);

            decimal currentEditionPriceForUnusedPeriod = 0;
            decimal targetEditionPriceForUnusedPeriod = 0;

            var currentEditionPrice = currentEdition.GetPaymentAmount(paymentPeriodType);
            var targetEditionPrice = targetEdition.GetPaymentAmount(paymentPeriodType);

            if (currentEditionPrice > 0)
            {
                currentEditionPriceForUnusedPeriod = currentEditionPrice * unusedPeriodCount;
                currentEditionPriceForUnusedPeriod += (currentEditionPrice / (int)paymentPeriodType) / numberOfHoursPerDay * unusedHoursCount;
            }

            if (targetEditionPrice > 0)
            {
                targetEditionPriceForUnusedPeriod = targetEditionPrice * unusedPeriodCount;
                targetEditionPriceForUnusedPeriod += (targetEditionPrice / (int)paymentPeriodType) / numberOfHoursPerDay * unusedHoursCount;
            }

            return targetEditionPriceForUnusedPeriod - currentEditionPriceForUnusedPeriod;
        }

        public async Task<Tenant> UpdateTenantAsync(int tenantId, bool isActive, bool? isInTrialPeriod, PaymentPeriodType? paymentPeriodType, int editionId, EditionPaymentType editionPaymentType)
        {
            var tenant = await FindByIdAsync(tenantId);

            tenant.IsActive = isActive;

            if (isInTrialPeriod.HasValue)
            {
                tenant.IsInTrialPeriod = isInTrialPeriod.Value;
            }

            tenant.EditionId = editionId;

            if (paymentPeriodType.HasValue)
            {
                tenant.UpdateSubscriptionDateForPayment(paymentPeriodType.Value, editionPaymentType);
            }

            return tenant;
        }

        public async Task<EndSubscriptionResult> EndSubscriptionAsync(Tenant tenant, SubscribableEdition edition, DateTime nowUtc)
        {
            if (tenant.EditionId == null || tenant.HasUnlimitedTimeSubscription())
            {
                throw new Exception($"Can not end tenant {tenant.TenancyName} subscription for {edition.DisplayName} tenant has unlimited time subscription!");
            }

            Debug.Assert(tenant.SubscriptionEndDateUtc != null, "tenant.SubscriptionEndDateUtc != null");

            var subscriptionEndDateUtc = tenant.SubscriptionEndDateUtc.Value;
            if (!tenant.IsInTrialPeriod)
            {
                subscriptionEndDateUtc = tenant.SubscriptionEndDateUtc.Value.AddDays(edition.WaitingDayAfterExpire ?? 0);
            }

            if (subscriptionEndDateUtc >= nowUtc)
            {
                throw new Exception($"Can not end tenant {tenant.TenancyName} subscription for {edition.DisplayName} since subscription has not expired yet!");
            }

            if (!tenant.IsInTrialPeriod && edition.ExpiringEditionId.HasValue)
            {
                tenant.EditionId = edition.ExpiringEditionId.Value;
                tenant.SubscriptionEndDateUtc = null;

                await UpdateAsync(tenant);

                return EndSubscriptionResult.AssignedToAnotherEdition;
            }

            tenant.IsActive = false;
            tenant.IsInTrialPeriod = false;

            await UpdateAsync(tenant);

            return EndSubscriptionResult.TenantSetInActive;
        }

        public override Task UpdateAsync(Tenant tenant)
        {
            if (tenant.IsInTrialPeriod && !tenant.SubscriptionEndDateUtc.HasValue)
            {
                throw new UserFriendlyException(LocalizationManager.GetString(ZeroConsts.LocalizationSourceName, "TrialWithoutEndDateErrorMessage"));
            }

            return base.UpdateAsync(tenant);
        }
        
        #region Customize
        public async Task CreateWithAdminUserAsync(int? parentId,
            string tenancyName,
            string name,
            string adminPassword,
            string adminEmailAddress,
            string connectionString,
            bool isActive,
            int? editionId,
            bool shouldChangePasswordOnNextLogin,
            bool sendActivationEmail,
            DateTime? subscriptionEndDate,
            bool isInTrialPeriod,
            string emailActivationLink)
        {
            int newTenantId;
            long newAdminId;

            await CheckEditionAsync(editionId, isInTrialPeriod);

            if (isInTrialPeriod && !subscriptionEndDate.HasValue)
                throw new UserFriendlyException(LocalizationManager.GetString(ZeroConsts.LocalizationSourceName, "TrialWithoutEndDateErrorMessage"));
            
            using (var uow = _unitOfWorkManager.Begin(TransactionScopeOption.RequiresNew))
            {
                //Create tenant
                var tenant = new Tenant(tenancyName, name)
                {
                    IsActive = isActive,
                    EditionId = editionId,
                    SubscriptionEndDateUtc = subscriptionEndDate?.ToUniversalTime(),
                    IsInTrialPeriod = isInTrialPeriod,
                    ConnectionString = connectionString.IsNullOrWhiteSpace() ? null : SimpleStringCipher.Instance.Encrypt(connectionString)
                };

                if (parentId > 0)
                    tenant.ParentId = parentId;
                
                tenant.Code = await GetNextChildCodeAsync(tenant.ParentId);
                
                await CreateAsync(tenant);
                await _unitOfWorkManager.Current.SaveChangesAsync(); 
                //To get new tenant's id.

                var lstEmailTemplate = await _emailTemplateRepository.GetAllListAsync(o => o.TenantId == null && !o.IsDeleted && o.AutoCreateForNewTenant);
                if (lstEmailTemplate.Any())
                {
                    foreach (var newEmpTemp in lstEmailTemplate.Select(emailTemp => new EmailTemplate
                    {
                        TenantId = tenant.Id,
                        Title = emailTemp.Title,
                        Content = emailTemp.Content,
                        AutoCreateForNewTenant = false,
                        IsActive = emailTemp.IsActive
                    }))
                    {
                        await _emailTemplateRepository.InsertAndGetIdAsync(newEmpTemp);
                    }    
                }

                //Create tenant database
                _abpZeroDbMigrator.CreateOrMigrateForTenant(tenant);

                //We are working entities of new tenant, so changing tenant filter
                using (_unitOfWorkManager.Current.SetTenantId(tenant.Id))
                {
                    //Create static roles for new tenant
                    CheckErrors(await _roleManager.CreateStaticRoles(tenant.Id));
                    await _unitOfWorkManager.Current.SaveChangesAsync(); //To get static role ids

                    //grant all permissions to admin role
                    var adminRole = _roleManager.Roles.Single(r => r.Name == StaticRoleNames.Tenants.Admin);
                    
                    var editionPermission = await _editionPermissionRepository.GetAll().Where(o => o.EditionId == editionId).Select(o => o.PermissionName).ToListAsync();
                    
                    var grantedPermissions = _permissionManager
                        .GetAllPermissions()
                        .Where(p => editionPermission.Contains(p.Name))
                        .ToList();
                    
                    var verifiedPermissions = StaticRolesHelper.AddRequiredPermissions(adminRole, grantedPermissions);
                    await _roleManager.SetGrantedPermissionsAsync(adminRole, verifiedPermissions);

                    //User role should be default
                    var userRole = _roleManager.Roles.Single(r => r.Name == StaticRoleNames.Tenants.User);
                    userRole.IsDefault = true;
                    CheckErrors(await _roleManager.UpdateAsync(userRole));

                    //Create admin user for the tenant
                    var adminUser = User.CreateTenantAdminUser(tenant.Id, adminEmailAddress);
                    adminUser.ShouldChangePasswordOnNextLogin = shouldChangePasswordOnNextLogin;
                    adminUser.IsActive = true;

                    if (adminPassword.IsNullOrEmpty())
                        adminPassword = await _userManager.CreateRandomPassword();
                    else
                    {
                        await _userManager.InitializeOptionsAsync(AbpSession.TenantId);
                        foreach (var validator in _userManager.PasswordValidators)
                        {
                            CheckErrors(await validator.ValidateAsync(_userManager, adminUser, adminPassword));
                        }

                    }

                    adminUser.Password = _passwordHasher.HashPassword(adminUser, adminPassword);

                    CheckErrors(await _userManager.CreateAsync(adminUser));
                    await _unitOfWorkManager.Current.SaveChangesAsync(); //To get admin user's id

                    //Assign admin user to admin role!
                    CheckErrors(await _userManager.AddToRoleAsync(adminUser, adminRole.Name));

                    //Notifications
                    await _appNotifier.WelcomeToTheApplicationAsync(adminUser);

                    //Send activation email
                    if (sendActivationEmail)
                    {
                        adminUser.SetNewEmailConfirmationCode();
                        try
                        {
                            await _userEmailer.SendEmailActivationLinkAsync(adminUser, emailActivationLink, adminPassword);
                        }
                        catch (Exception e)
                        {
                            _logger.Error($"Create Tenant - Send Active Email - Active Link : {emailActivationLink} - Admin Psw : {adminPassword}", e);
                        }
                        
                    }

                    await _unitOfWorkManager.Current.SaveChangesAsync();

                    await _demoDataBuilder.BuildForAsync(tenant);

                    newTenantId = tenant.Id;
                    newAdminId = adminUser.Id;
                }

                await uow.CompleteAsync();
            }

            //Used a second UOW since UOW above sets some permissions and _notificationSubscriptionManager.SubscribeToAllAvailableNotificationsAsync needs these permissions to be saved.
            using (var uow = _unitOfWorkManager.Begin(TransactionScopeOption.RequiresNew))
            {
                using (_unitOfWorkManager.Current.SetTenantId(newTenantId))
                {
                    await _notificationSubscriptionManager.SubscribeToAllAvailableNotificationsAsync(new UserIdentifier(newTenantId, newAdminId));
                    await _unitOfWorkManager.Current.SaveChangesAsync();
                    await uow.CompleteAsync();
                }
            }
        }
        
        [UnitOfWork]
        public virtual async Task MoveAsync(int id, int? parentId)
        {
            var tenant = await _tenantRepository.GetAsync(id);
            if (parentId.HasValue && parentId > 0)
            {
                if (tenant.ParentId == parentId)
                {
                    return;
                }

                //Should find children before Code change
                var children = await FindChildrenAsync(id, true);

                //Store old code
                var oldCode = tenant.Code;

                //Move
                tenant.Code = await GetNextChildCodeAsync(parentId);
                tenant.ParentId = parentId;

                //Update Children Codes
                foreach (var child in children)
                {
                    child.Code = Tenant.AppendCode(tenant.Code, Tenant.GetRelativeCode(child.Code, oldCode));
                }
            }
            else
            {
                // All root node
                var lstRootNode = _tenantRepository.GetAll()
                    .Where(o => o.ParentId == null)
                    .OrderBy(o => o.Code).ToList();
                if (lstRootNode.Any())
                {
                    if (lstRootNode.Count == 1)
                        return;
                    var beforeNode = new Tenant();
                    for (var i = 0; i < lstRootNode.Count; i++)
                    {
                        if (lstRootNode[i].Id == tenant.Id && i >0)
                        {
                            beforeNode = lstRootNode[i-1];
                        }
                    }

                    if (beforeNode.Id > 0)
                    {
                        //Swap children code
                        var children = await FindChildrenAsync(id, true);
                        var beforeChildren = await FindChildrenAsync(beforeNode.Id, true);
                        if (children != null && children.Any())
                        {
                            foreach (var h in children)
                            {
                                var oldCode = h.Code.Split(".");
                                oldCode[0] = beforeNode.Code;
                                h.Code = string.Join(".", oldCode);
                            }
                        }
                        if (beforeChildren != null && beforeChildren.Any())
                        {
                            foreach (var h in beforeChildren)
                            {
                                var oldCode = h.Code.Split(".");
                                oldCode[0] = tenant.Code;
                                h.Code = string.Join(".", oldCode);
                            }
                        }
                        // Swap code.
                        var tempCode = beforeNode.Code;
                        beforeNode.Code = tenant.Code;
                        tenant.Code = tempCode;
                    }
                }
            }
        }

        private async Task<List<Tenant>> FindChildrenAsync(int? parentId, bool recursive = false)
        {
            if (!recursive)
            {
                return await _tenantRepository.GetAllListAsync(ou => ou.ParentId == parentId);
            }

            if (!parentId.HasValue)
            {
                return await _tenantRepository.GetAllListAsync();
            }

            var code = await GetCodeAsync(parentId.Value);

            return await _tenantRepository.GetAllListAsync(
                ou => ou.Code.StartsWith(code) && ou.Id != parentId.Value
            );
        }

        protected virtual async Task<string> GetNextChildCodeAsync(int? parentId)
        {
            var lastChild = await GetLastChildOrNullAsync(parentId);
            if (lastChild == null)
            {
                var parentCode = parentId != null ? await GetCodeAsync(parentId.Value) : null;
                return Tenant.AppendCode(parentCode, Tenant.CreateCode(1));
            }

            return Tenant.CalculateNextCode(lastChild.Code);
        }

        protected virtual async Task<Tenant> GetLastChildOrNullAsync(int? parentId)
        {
            var children = await _tenantRepository.GetAllListAsync(ou => ou.ParentId == parentId);
            return children.OrderBy(c => c.Code).LastOrDefault();
        }

        protected virtual async Task<string> GetCodeAsync(int id)
        {
            return (await _tenantRepository.GetAsync(id)).Code;
        }
        #endregion
    }
}
