using System;
using System.Collections.Generic;
using System.Reflection;

namespace CrossStitchRework;

internal sealed class BetterBindingsBridge : IDisposable
{
    private const string ApiTypeName = "BetterBindings.BetterBindingsApi, BetterBindings";

    private IDisposable? registration;

    internal bool IsRegistered => registration != null;

    internal bool TryRegister(Action onPressed, Func<bool> canInvoke)
    {
        Type? apiType = Type.GetType(ApiTypeName, throwOnError: false);
        if (apiType == null)
        {
            return false;
        }

        MethodInfo? register = apiType.GetMethod(
            "RegisterShortcut",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            new[]
            {
                typeof(string),
                typeof(string),
                typeof(IReadOnlyDictionary<string, string>),
                typeof(Action),
                typeof(Func<bool>),
                typeof(int),
            },
            modifiers: null);
        if (register == null)
        {
            Plugin.Log.LogWarning("Better Bindings is installed, but its RegisterShortcut API is unavailable; using equipped-skill input.");
            return false;
        }

        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase)
        {
            ["EN"] = "Cross Stitch Guard",
            ["ZH"] = "十字绣格挡",
            ["ZH_TW"] = "十字繡格擋",
        };

        try
        {
            object? result = register.Invoke(
                null,
                new object?[]
                {
                    Plugin.PluginGuid,
                    "CrossStitchGuard",
                    names,
                    onPressed,
                    canInvoke,
                    100,
                });
            registration = result as IDisposable;
            if (registration == null)
            {
                Plugin.Log.LogWarning("Better Bindings returned no disposable shortcut registration; using equipped-skill input.");
                return false;
            }

            Plugin.Log.LogInfo("Registered the independent Cross Stitch guard shortcut with Better Bindings.");
            return true;
        }
        catch (TargetInvocationException exception)
        {
            Plugin.Log.LogError($"Better Bindings rejected the Cross Stitch shortcut registration: {exception.InnerException ?? exception}");
            return false;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError($"Could not register the Cross Stitch shortcut with Better Bindings: {exception}");
            return false;
        }
    }

    public void Dispose()
    {
        registration?.Dispose();
        registration = null;
    }
}
