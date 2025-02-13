﻿using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;

namespace Kros.Swagger.Extensions
{
    /// <summary>
    /// Adds enum value descriptions to Swagger.
    /// </summary>
    public class EnumDocumentFilter : IDocumentFilter
    {
        /// <inheritdoc />
        public void Apply(SwaggerDocument swaggerDoc, DocumentFilterContext context)
        {
            foreach (Schema schema in swaggerDoc.Definitions.Values)
            {
                foreach (Schema property in schema.Properties.Values)
                {
                    IList<object> propertyEnums = property.Enum;

                    if (propertyEnums != null && propertyEnums.Count > 0)
                    {
                        property.Description += DescribeEnum(propertyEnums);
                    }
                }
            }

            if (swaggerDoc.Paths.Count > 0)
            {
                foreach (PathItem pathItem in swaggerDoc.Paths.Values)
                {
                    DescribeEnumParameters(pathItem.Parameters);

                    var possibleParameterisedOperations = new List<Operation> { pathItem.Get, pathItem.Post, pathItem.Put };
                    possibleParameterisedOperations
                        .FindAll(x => x != null)
                        .ForEach(x => DescribeEnumParameters(x.Parameters));
                }
            }
        }

        private static void DescribeEnumParameters(IList<IParameter> parameters)
        {
            if (parameters is null)
            {
                return;
            }

            foreach (IParameter param in parameters)
            {
                if (param.Extensions.ContainsKey("enum") && (param.Extensions["enum"] is IList<object> paramEnums) &&
                    (paramEnums.Count > 0))
                {
                    param.Description += DescribeEnum(paramEnums);
                }
            }
        }

        private static string DescribeEnum(IEnumerable<object> enums)
        {
            var enumDescriptions = new List<string>();
            Type type = null;
            foreach (object enumOption in enums)
            {
                if (type == null)
                {
                    type = enumOption.GetType();
                }
                enumDescriptions.
                    Add($"{Convert.ChangeType(enumOption, type.GetEnumUnderlyingType())} = {Enum.GetName(type, enumOption)}");
            }

            return $"{Environment.NewLine}{string.Join(Environment.NewLine, enumDescriptions)}";
        }
    }
}
