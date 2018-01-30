using System;
using Microsoft.CodeAnalysis;

namespace DataObjectHelper
{
    public abstract class Case
    {
        private Case()
        {
        }

        public class ClassCase : Case
        {
            public ClassCase(INamedTypeSymbol symbol)
            {
                Symbol = symbol;
            }

            public INamedTypeSymbol Symbol { get; }
        }

        public class FSharpNullCase : Case
        {
            public FSharpNullCase(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }

        public TResult Match<TResult>(Func<ClassCase, TResult> caseClassCase,
            Func<FSharpNullCase, TResult> caseFSharpNullCase)
        {
            if (this is ClassCase classCase)
                return caseClassCase(classCase);
            else if (this is FSharpNullCase fSharpNullCase)
                return caseFSharpNullCase(fSharpNullCase);

            throw new Exception("Invalid type");
        }

        public void Match(Action<ClassCase> caseClassCase,
            Action<FSharpNullCase> caseFSharpNullCase)
        {
            if (this is ClassCase classCase)
            {
                caseClassCase(classCase);
                return;
            }

            if (this is FSharpNullCase fSharpNullCase)
            {
                caseFSharpNullCase(fSharpNullCase);
                return;
            }

            throw new Exception("Invalid type");
        }
    }
}