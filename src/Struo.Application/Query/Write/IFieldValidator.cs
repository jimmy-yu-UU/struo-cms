// src/Struo.Application/Query/Write/IFieldValidator.cs
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Query;

/// <summary>
/// Per-<see cref="Struo.Domain.Metadata.Enums.FieldInterface"/> validate/normalize strategy for a
/// single parent-entity field. The implementation reads the current value off <paramref name="entity"/>
/// via <paramref name="pi"/>, validates it (throwing <see cref="Struo.Domain.Query.QueryException"/> on
/// a client error), and writes the normalized value back via <c>pi.SetValue</c>.
/// </summary>
public interface IFieldValidator
{
    void ValidateAndNormalize(FieldMetadata field, System.Reflection.PropertyInfo pi, object entity);
}
