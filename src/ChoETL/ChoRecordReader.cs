﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChoETL
{
    public abstract class ChoRecordReader
    {
        public readonly Type RecordType;
        internal TraceSwitch TraceSwitch;

        public ChoRecordReader(Type recordType)
        {
            ChoGuard.ArgumentNotNull(recordType, "RecordType");

            RecordType = recordType;
        }

        public abstract IEnumerable<object> AsEnumerable(object source, Func<object, bool?> filterFunc = null);
    }
}
