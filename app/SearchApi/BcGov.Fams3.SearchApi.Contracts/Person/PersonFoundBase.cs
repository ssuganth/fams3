﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace BcGov.Fams3.SearchApi.Contracts.Person
{
    public abstract class PersonalInfo
    {
        [Description("The related dates information of the identifier")]
        public IEnumerable<ReferenceDate> ReferenceDates { get; set; }
    }
}
