﻿using DPS.Lib.Application.Shared.Dto.Basic.Rfid.RfidType;

namespace Zero.Web.Areas.Lib.Model.RfidType
{
    public class CreateOrEditRfidTypeViewModel
    {
        public CreateOrEditRfidTypeDto RfidType { get; set; }

        public bool IsEditMode => RfidType.Id.HasValue;
    }
}