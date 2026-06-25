// src/Struo.Domain/Query/QueryOperator.cs
namespace Struo.Domain.Query;

public enum QueryOperator
{
    Eq, Neq, In, Nin, Lt, Lte, Gt, Gte, Null, NNull, Contains, StartsWith, EndsWith
}
