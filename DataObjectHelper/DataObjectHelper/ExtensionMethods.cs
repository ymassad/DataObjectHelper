using System;
using System.Collections.Generic;
using System.Linq;

namespace DataObjectHelper
{
    public static class ExtensionMethods
    {
        public static Maybe<T> FirstOrNoValue<T>(this IEnumerable<T> enumerable)
        {
            foreach (var item in enumerable)
            {
                return item;
            }

            return Maybe<T>.NoValue();
        }

        public static Maybe<T> FirstOrNoValue<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate)
        {
            foreach (var item in enumerable)
            {
                if(predicate(item))
                    return item;
            }

            return Maybe<T>.NoValue();
        }

        public static Maybe<T> If<T>(this Maybe<T> maybe, Func<T, bool> condition)
        {
            if (maybe.HasNoValue)
                return maybe;

            if (condition(maybe.GetValue()))
                return maybe;

            return Maybe<T>.NoValue();
        }

        public static Maybe<T> ToMaybe<T>(this T value)
        {
            return value;
        }

        public static ToBeCasted<T> TryCast<T>(this T value) where T:class
        {
            return new ToBeCasted<T>(value);
        }


        public static ToBeCastedMaybe<T> TryCast<T>(this Maybe<T> maybe) where T : class
        {
            return new ToBeCastedMaybe<T>(maybe);
        }

        public static Maybe<T[]> Traverse<T>(this Maybe<T>[] enumerable)
        {
            List<T> list = new List<T>();

            foreach (var item in enumerable)
            {
                if(item.HasNoValue)
                    return Maybe<T[]>.NoValue();

                list.Add(item.GetValue());
            }

            return list.ToArray();
        }


        public static Maybe<TResult> ChainValues<T1, T2, TResult>(this (Maybe<T1> maybe1, Maybe<T2> maybe2) maybes,
            Func<T1, T2, TResult> function)
        {
            if(maybes.maybe1.HasNoValue)
                return Maybe<TResult>.NoValue();

            if(maybes.maybe2.HasNoValue)
                return Maybe<TResult>.NoValue();

            return function(maybes.maybe1.GetValue(), maybes.maybe2.GetValue());
        }

        public static Maybe<TResult> ChainValues<T1, T2, TResult>(this (Maybe<T1> maybe1, Maybe<T2> maybe2) maybes,
            Func<T1, T2, Maybe<TResult>> function)
        {
            if (maybes.maybe1.HasNoValue)
                return Maybe<TResult>.NoValue();

            if (maybes.maybe2.HasNoValue)
                return Maybe<TResult>.NoValue();

            return function(maybes.maybe1.GetValue(), maybes.maybe2.GetValue());
        }

        public static Maybe<TResult> ChainValues<T1, T2, T3, TResult>(this (Maybe<T1> maybe1, Maybe<T2> maybe2, Maybe<T3> maybe3) maybes,
            Func<T1, T2, T3, TResult> function)
        {
            if (maybes.maybe1.HasNoValue)
                return Maybe<TResult>.NoValue();

            if (maybes.maybe2.HasNoValue)
                return Maybe<TResult>.NoValue();

            if (maybes.maybe3.HasNoValue)
                return Maybe<TResult>.NoValue();

            return function(maybes.maybe1.GetValue(), maybes.maybe2.GetValue(), maybes.maybe3.GetValue());
        }

        public static Maybe<TResult> ChainValues<T1, T2, T3, TResult>(this (Maybe<T1> maybe1, Maybe<T2> maybe2, Maybe<T3> maybe3) maybes,
            Func<T1, T2, T3, Maybe<TResult>> function)
        {
            if (maybes.maybe1.HasNoValue)
                return Maybe<TResult>.NoValue();

            if (maybes.maybe2.HasNoValue)
                return Maybe<TResult>.NoValue();

            if (maybes.maybe3.HasNoValue)
                return Maybe<TResult>.NoValue();

            return function(maybes.maybe1.GetValue(), maybes.maybe2.GetValue(), maybes.maybe3.GetValue());
        }

        public static bool EqualsAny(this string str, params string[] strings)
        {
            var comparer = StringComparer.OrdinalIgnoreCase;

            return strings.Any(s => comparer.Equals(str, s));
        }

        public static (Maybe<T1> maybe1, Maybe<T2> maybe2) With<T1, T2>(this Maybe<T1> maybe1, Maybe<T2> maybe2)
        {
            return (maybe1, maybe2);
        }

        public static (Maybe<T1> maybe1, Maybe<T2> maybe2, Maybe<T3> maybe3) With<T1, T2, T3>(this (Maybe<T1> maybe1, Maybe<T2> maybe2) maybes, Maybe<T3> maybe3)
        {
            return (maybes.maybe1, maybes.maybe2, maybe3);
        }

        public static bool HasValueAnd<T>(this Maybe<T> maybe, Func<T, bool> condition)
        {
            if (maybe.HasNoValue)
                return false;

            return condition(maybe.GetValue());
        }
    }
}