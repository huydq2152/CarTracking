﻿using Abp.Application.Services.Dto;

namespace DPS.Lib.Application.Shared.Dto.Transport.PointType
{
    public class PointTypeDto: FullAuditedEntityDto
    {
        public int? TenantId { get; set; }
        
        public string Code { get; set; }
        
        public string Name { get; set; }
        
        public string Note { get; set; }
    }
}