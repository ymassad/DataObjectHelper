namespace DataObjectHelper
{
    public class ToBeCasted<T>
    {
        private T value;

        public ToBeCasted(T value)
        {
            this.value = value;
        }

        public Maybe<TTo> To<TTo>() where TTo:class
        {
            var casted = value as TTo;

            return casted;
        }
    }
}