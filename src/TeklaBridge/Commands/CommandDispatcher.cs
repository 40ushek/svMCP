using System.IO;
using Tekla.Structures.Model;

namespace TeklaBridge.Commands;

internal sealed class CommandDispatcher
{
    private readonly ICommandHandler[] _handlers;

    public CommandDispatcher(Model model, TextWriter output)
    {
        _handlers = new ICommandHandler[]
        {
            new ModelCommandHandler(model, output),
            new DrawingCommandHandler(model, output)
        };
    }

    public bool Dispatch(string command, string[] args)
    {
        foreach (var handler in _handlers)
            if (handler.TryHandle(command, args))
                return true;
        return false;
    }
}
