using System;
using System.Threading.Tasks;

namespace DataObjectHelper
{
    public static class Maybe
    {
        public class NoValueClass
        {
            
        }

        public static NoValueClass NoValue { get; } = new NoValueClass();
    }

    public struct Maybe<T>
    {
        private bool hasValue;

        private T value;

        private Maybe(bool hasValue, T value)
        {
            this.hasValue = hasValue;
            this.value = value;
        }

        public bool HasValue => hasValue;

        public bool HasNoValue => !hasValue;

        public T GetValue() => value;

        public static Maybe<T> OfValue(T value)
        {
            if(value == null)
                throw new Exception("Value cannot be null");

            return new Maybe<T>(true, value);
        }

        public static Maybe<T> NoValue()
        {
            return new Maybe<T>(false, default);
        }

        public static implicit operator Maybe<T>(T value)
        {
            if (value == null)
                return Maybe<T>.NoValue();

            return Maybe<T>.OfValue(value);
        }

        public static implicit operator Maybe<T>(Maybe.NoValueClass noValue)
        {
            return Maybe<T>.NoValue();
        }

        public Maybe<TTo> ChainValue<TTo>(Func<T, TTo> converter)
        {
            if (!hasValue)
                return Maybe<TTo>.NoValue();

            return converter(value);
        }

        public async Task<Maybe<TTo>> ChainValue<TTo>(Func<T, Task<TTo>> converter)
        {
            if (!hasValue)
                return Maybe<TTo>.NoValue();

            return await converter(value);
        }


        public Maybe<TTo> ChainValue<TTo>(Func<T, Maybe<TTo>> converter)
        {
            if (!hasValue)
                return Maybe<TTo>.NoValue();

            return converter(value);
        }

        public T ValueOr(T defaultValue)
        {
            if (hasValue)
                return value;

            return defaultValue;
        }

        public T ValueOr(Func<T> defaultValueFactory)
        {
            if (hasValue)
                return value;

            return defaultValueFactory();
        }

        public Maybe<T> ValueOrMaybe(Func<Maybe<T>> defaultValueFactory)
        {
            if (hasValue)
                return this;

            return defaultValueFactory();
        }
    }
}