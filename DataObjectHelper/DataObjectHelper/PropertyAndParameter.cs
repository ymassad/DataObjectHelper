using Microsoft.CodeAnalysis;

namespace DataObjectHelper
{
    public class PropertyAndParameter
    {
        public IParameterSymbol Parameter { get; }
        public IPropertySymbol Property { get; }

        public PropertyAndParameter(IParameterSymbol parameter, IPropertySymbol property)
        {
            Parameter = parameter;
            Property = property;
        }
    }
}