namespace DataObjectHelper
{
    public class ToBeCastedMaybe<T>
    {
        private readonly Maybe<T> maybe;

        public ToBeCastedMaybe(Maybe<T> maybe)
        {
            this.maybe = maybe;
        }


        public Maybe<TTo> To<TTo>() where TTo : class
        {
            if(maybe.HasNoValue)
                return Maybe<TTo>.NoValue();

            var casted = maybe.GetValue() as TTo;

            return casted;
        }

    }
}