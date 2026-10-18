//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

// See https://aka.ms/new-console-template for more information
using Grpc.Net.Client;
using NDMicroCpu.Debugger.Contracts;
using ProtoBuf.Grpc.Client;


namespace NDMicroCpu.Debugger.Client;

internal class Program
{
    private static int Main(string[] args)
    {
        using var channel = GrpcChannel.ForAddress("http://localhost:9000");
        var client = channel.CreateGrpcService<IDebuggerService>();

        var reply = client.Step(
            new StepRequest { Steps = 1 }).Result;

        Console.WriteLine($"Ran: {reply.Steps}");
        Console.WriteLine($"P is: {reply.P}");

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();

        return 0;
    }
}
