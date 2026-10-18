# ndmicrodebug-client - the smallest gRPC client

Connects to `ndmicrodebug` on `http://localhost:9000`, asks it to step one macro instruction and
prints how many steps ran and the program counter. It exists to show how a client is written:
reference `NDMicroCpu.Debugger.Contracts`, open a `GrpcChannel`, and call
`CreateGrpcService<IDebuggerService>()`.

## Run it

```powershell
# from the repository root, with ndmicrodebug already running
dotnet run --project src\NDMicroCpu.Debugger.Client
```
