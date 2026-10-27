// src/Struo.Api/Hosting/StruoCmsBuilder.cs
namespace Struo.Api.Hosting;

/// <summary>
/// The hook object handed to the optional callback of
/// <see cref="StruoCmsHostingExtensions.AddStruoCms"/>. It is the place where a fork reaches into core
/// registration; it carries no state of its own yet.
/// </summary>
public sealed class StruoCmsBuilder;
