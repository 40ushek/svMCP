namespace TeklaBridge.Commands;

internal interface ICommandHandler
{
    bool TryHandle(string command, string[] args);
}
