﻿using System.Collections.Generic;
using System.Text.Json;

namespace DeltaKustoLib.SchemaObjects
{
    public class DatabaseSchema
    {
        public IDictionary<string, FunctionSchema> Functions { get; set; } = new Dictionary<string, FunctionSchema>();
    }
}