using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

/// <summary>
/// Minimal offline stand-in for HowToFishTrainer.App.Licensing.ILicenseClient.
/// Every member is answered locally; no network or activation cache is touched. Built with
/// reflection so the probe stays independent of the product assembly at compile time.
/// </summary>
public class FakeLicenseClient : DispatchProxy
{
    private object? _status;

    public static object Create(Type interfaceType, Type statusType, bool sponsor)
    {
        // DispatchProxy exposes two Create overloads in .NET 8, so pick the parameterless one by shape.
        MethodInfo factory = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(method => method.Name == nameof(DispatchProxy.Create)
                             && method.IsGenericMethodDefinition
                             && method.GetGenericArguments().Length == 2
                             && method.GetParameters().Length == 0);
        object proxy = factory.MakeGenericMethod(interfaceType, typeof(FakeLicenseClient)).Invoke(null, null)!;
        FakeLicenseClient instance = (FakeLicenseClient)proxy;
        instance._status = statusType.GetMethod("Standard", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object?[] { sponsor ? "本地测试版" : "标准版" });
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null)
        {
            return null;
        }

        string name = targetMethod.Name;
        if (name == "get_Current")
        {
            return _status;
        }

        if (name == "get_DeviceId")
        {
            return "UI-PROBE-LOCAL-DEVICE";
        }

        if (name is "add_StatusChanged" or "remove_StatusChanged")
        {
            return null;
        }

        Type returnType = targetMethod.ReturnType;
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            Type resultType = returnType.GetGenericArguments()[0];
            if (resultType == typeof(string))
            {
                return Task.FromResult("UI-PROBE-LOCAL-DEVICE");
            }

            object? value = resultType.Name == "LicenseActivationResult"
                ? Activator.CreateInstance(resultType, new object?[] { false, _status })
                : _status;
            MethodInfo fromResult = typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType);
            return fromResult.Invoke(null, new[] { value });
        }

        if (returnType == typeof(void))
        {
            return null;
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }
}
