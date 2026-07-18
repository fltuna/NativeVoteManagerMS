using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace NativeVoteManagerMS;

/// <summary>
/// Guards invocations of code provided by external modules (vote handlers, pass conditions,
/// culture providers, menu/permission/localizer compats) so their exceptions cannot corrupt
/// the vote manager state or leak across the managed/native boundary.
/// </summary>
internal static class ExternalCall
{
    public static void Run(ILogger logger, Action action,
        [CallerArgumentExpression(nameof(action))] string? callbackName = null)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unhandled exception in external callback: {Callback}", callbackName);
        }
    }

    public static T Run<T>(ILogger logger, Func<T> func, T fallback,
        [CallerArgumentExpression(nameof(func))] string? callbackName = null)
    {
        try
        {
            return func();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unhandled exception in external callback: {Callback}", callbackName);
            return fallback;
        }
    }
}
