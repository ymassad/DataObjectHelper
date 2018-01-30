using System;

namespace DataObjectHelper
{
    public abstract class Constructor
    {
        private Constructor()
        {
        }

        public class NormalConstructor : Constructor
        {
        }

        public class FSharpUnionCaseNewMethod : Constructor
        {

        }

        public TResult Match<TResult>(Func<TResult> caseNormalConstructor,
            Func<TResult> caseFSharpUnionCaseNewMethod)
        {
            if (this is NormalConstructor)
                return caseNormalConstructor();
            if (this is FSharpUnionCaseNewMethod)
                return caseFSharpUnionCaseNewMethod();

            throw new Exception("Invalid type");
        }

        public void Match(Action caseNormalConstructor,
            Action caseFSharpUnionCaseNewMethod)
        {
            if (this is NormalConstructor)
            {
                caseNormalConstructor();
                return;
            }
            else if (this is FSharpUnionCaseNewMethod)
            {
                caseFSharpUnionCaseNewMethod();
                return;
            }

            throw new Exception("Invalid type");
        }
    }
}