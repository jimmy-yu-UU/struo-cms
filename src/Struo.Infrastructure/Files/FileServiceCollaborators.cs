using Struo.Application.Abstractions;
using Struo.Application.Localization;
using Struo.Application.Query;

namespace Struo.Infrastructure.Files;

/// <summary>The item-side collaborators FileService needs, grouped so its constructor stays within
/// the parameter-count guideline; registered as scoped by AddStruoFiles.</summary>
public sealed record FileServiceCollaborators(
    IItemRepository Repository, ILanguageProvider Languages, ICurrentUserAccessor CurrentUser);
