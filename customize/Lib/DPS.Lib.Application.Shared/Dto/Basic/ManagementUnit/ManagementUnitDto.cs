﻿using Abp.Application.Services.Dto;

namespace DPS.Lib.Application.Shared.Dto.Basic.ManagementUnit
{
    public class ManagementUnitDto: FullAuditedEntityDto
    {
        public int? TenantId { get; set; }
        
        public string Code { get; set; }
        
        public string Name { get; set; }
        
        public string Note { get; set; }
    }
}