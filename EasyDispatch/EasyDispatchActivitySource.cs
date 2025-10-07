using System.Diagnostics;
using System.Reflection;

namespace EasyDispatch;

/// <summary>
/// ActivitySource for EasyDispatch tracing and observability.
/// </summary>
internal static class EasyDispatchActivitySource
{
	/// <summary>
	/// The name of the ActivitySource used for all EasyDispatch operations.
	/// </summary>
	public const string SourceName = "EasyDispatch";

	/// <summary>
	/// The version of the ActivitySource, automatically derived from the assembly version.
	/// </summary>
	public static readonly string Version = typeof(EasyDispatchActivitySource).Assembly
		.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
		?.InformationalVersion
		?? typeof(EasyDispatchActivitySource).Assembly.GetName().Version?.ToString()
		?? "1.0.0";

	/// <summary>
	/// The ActivitySource instance for creating activities.
	/// </summary>
	public static readonly ActivitySource Source = new(SourceName, Version);
}