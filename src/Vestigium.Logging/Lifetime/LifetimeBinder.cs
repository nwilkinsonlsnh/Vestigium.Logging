using System.Linq.Expressions;
using System.Reflection;

namespace Vestigium.Logging;

/// <summary>
/// Subscribes to an instance event named <c>Exit</c> without referencing WPF.
/// Compatible with <c>EventHandler</c> and WPF <c>ExitEventHandler</c>.
/// </summary>
internal sealed class LifetimeBinder
{
    private readonly object _gate = new();
    private object? _instance;
    private EventInfo? _event;
    private Delegate? _handler;

    public void BindExit(object? instance, Action onExit)
    {
        ArgumentNullException.ThrowIfNull(onExit);
        lock (_gate)
        {
            UnbindCore();
            if (instance is null)
                return;

            var evt = instance.GetType().GetEvent("Exit", BindingFlags.Instance | BindingFlags.Public);
            if (evt?.EventHandlerType is null)
                return;

            var invoke = evt.EventHandlerType.GetMethod("Invoke");
            if (invoke is null)
                return;

            var parameters = invoke.GetParameters();
            if (parameters.Length != 2)
                return;
            if (parameters[0].ParameterType != typeof(object))
                return;
            if (!typeof(EventArgs).IsAssignableFrom(parameters[1].ParameterType)
                && parameters[1].ParameterType != typeof(EventArgs))
                return;

            var senderP = Expression.Parameter(parameters[0].ParameterType, "sender");
            var argsP = Expression.Parameter(parameters[1].ParameterType, "args");
            var body = Expression.Invoke(Expression.Constant(onExit));
            var lambda = Expression.Lambda(evt.EventHandlerType, body, senderP, argsP);
            var handler = lambda.Compile();

            evt.AddEventHandler(instance, handler);
            _instance = instance;
            _event = evt;
            _handler = handler;
        }
    }

    public void Unbind()
    {
        lock (_gate)
            UnbindCore();
    }

    private void UnbindCore()
    {
        if (_instance is null || _event is null || _handler is null)
            return;
        try
        {
            _event.RemoveEventHandler(_instance, _handler);
        }
        catch
        {
            // Instance may already be gone.
        }

        _instance = null;
        _event = null;
        _handler = null;
    }
}
