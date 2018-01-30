using System.Collections.Immutable;

namespace DataObjectHelper
{
    public class TypeConstructorDetails
    {
        public TypeConstructorDetails(Constructor constructor, ImmutableArray<PropertyAndParameter> propertyAndParameters)
        {
            Constructor = constructor;
            PropertyAndParameters = propertyAndParameters;
        }

        public Constructor Constructor { get; }

        public ImmutableArray<PropertyAndParameter> PropertyAndParameters { get; }
    }
}