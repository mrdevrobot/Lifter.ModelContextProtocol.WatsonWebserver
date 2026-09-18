using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.WatsonWebserver;

/// <summary>
/// Fills an <see cref="McpServerOptions"/> with tools and prompts without the SDK's hosting
/// package, which only exists for dependency-injection hosts.
/// </summary>
public static class McpServerOptionsExtensions
{
    private const DynamicallyAccessedMemberTypes PrimitiveMembers =
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods |
        DynamicallyAccessedMemberTypes.PublicConstructors;

    /// <summary>
    /// Adds every method of <typeparamref name="TToolType"/> marked with
    /// <see cref="McpServerToolAttribute"/>. Instance methods get a target built from
    /// <paramref name="services"/>, or from the type's public constructor when there is none.
    /// </summary>
    public static McpServerOptions WithTools<[DynamicallyAccessedMembers(PrimitiveMembers)] TToolType>(
        this McpServerOptions options,
        IServiceProvider? services = null)
        => options.WithTools(typeof(TToolType), services);

    /// <summary>
    /// Adds every method of <paramref name="toolType"/> marked with
    /// <see cref="McpServerToolAttribute"/>.
    /// </summary>
    public static McpServerOptions WithTools(
        this McpServerOptions options,
        [DynamicallyAccessedMembers(PrimitiveMembers)] Type toolType,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(toolType);

        var createOptions = new McpServerToolCreateOptions { Services = services };
        options.ToolCollection ??= new McpServerPrimitiveCollection<McpServerTool>();

        foreach (var method in EnumerateMethods(toolType))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>() is null)
            {
                continue;
            }

            options.ToolCollection.Add(method.IsStatic
                ? McpServerTool.Create(method, target: null, createOptions)
                : McpServerTool.Create(method, _ => CreateTarget(toolType, services), createOptions));
        }

        EnsureToolCapability(options);
        return options;
    }

    /// <summary>Adds a single tool from a delegate.</summary>
    public static McpServerOptions WithTool(
        this McpServerOptions options,
        Delegate method,
        McpServerToolCreateOptions? createOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(method);

        options.ToolCollection ??= new McpServerPrimitiveCollection<McpServerTool>();
        options.ToolCollection.Add(McpServerTool.Create(method, createOptions));
        EnsureToolCapability(options);
        return options;
    }

    /// <summary>
    /// Adds every method of <typeparamref name="TPromptType"/> marked with
    /// <see cref="McpServerPromptAttribute"/>.
    /// </summary>
    public static McpServerOptions WithPrompts<[DynamicallyAccessedMembers(PrimitiveMembers)] TPromptType>(
        this McpServerOptions options,
        IServiceProvider? services = null)
        => options.WithPrompts(typeof(TPromptType), services);

    /// <summary>
    /// Adds every method of <paramref name="promptType"/> marked with
    /// <see cref="McpServerPromptAttribute"/>.
    /// </summary>
    public static McpServerOptions WithPrompts(
        this McpServerOptions options,
        [DynamicallyAccessedMembers(PrimitiveMembers)] Type promptType,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(promptType);

        var createOptions = new McpServerPromptCreateOptions { Services = services };
        options.PromptCollection ??= new McpServerPrimitiveCollection<McpServerPrompt>();

        foreach (var method in EnumerateMethods(promptType))
        {
            if (method.GetCustomAttribute<McpServerPromptAttribute>() is null)
            {
                continue;
            }

            options.PromptCollection.Add(method.IsStatic
                ? McpServerPrompt.Create(method, target: null, createOptions)
                : McpServerPrompt.Create(method, _ => CreateTarget(promptType, services), createOptions));
        }

        EnsurePromptCapability(options);
        return options;
    }

    /// <summary>Adds a single prompt from a delegate.</summary>
    public static McpServerOptions WithPrompt(
        this McpServerOptions options,
        Delegate method,
        McpServerPromptCreateOptions? createOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(method);

        options.PromptCollection ??= new McpServerPrimitiveCollection<McpServerPrompt>();
        options.PromptCollection.Add(McpServerPrompt.Create(method, createOptions));
        EnsurePromptCapability(options);
        return options;
    }

    private static MethodInfo[] EnumerateMethods(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static object CreateTarget(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type,
        IServiceProvider? services)
        => services is null ? Activator.CreateInstance(type)! : ActivatorUtilities.CreateInstance(services, type);

    private static void EnsureToolCapability(McpServerOptions options)
    {
        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Tools ??= new ToolsCapability();
    }

    private static void EnsurePromptCapability(McpServerOptions options)
    {
        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Prompts ??= new PromptsCapability();
    }
}
