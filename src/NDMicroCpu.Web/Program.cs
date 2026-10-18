//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Net.Http;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NDMicroCpu.Core.Common;
using NDMicroCpu.Web;
using NDMicroCpu.Web.Services;

internal class Program
{
    private static async global::System.Threading.Tasks.Task Main(string[] args)
    {

        // Production web default: keep the browser console usable and the CPU fast. LogLevel.CPU
        // firehosed EVERY fetch + memory read/write into the console (thousands per frame), which
        // both spammed the console and crippled WASM. Warning shows only genuine warnings/errors;
        // raise this (e.g. to CPU/Machine) only when actively debugging the emulator.
        Logger.SetDebuggerDestination(Logger.LogDestination.Console);
        Logger.SetDestination(Logger.LogDestination.Console);
        Logger.SetLogLevel(Logger.LogLevel.Warning);

        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

        // Add internal services
        builder.Services.AddSingleton<IAppService, AppService>();

        // Mud components
        builder.Services.AddMudServices();

        await builder.Build().RunAsync();


        // https://mudblazor.com/components/alert#api
        // https://github.com/MudBlazor/MudBlazor
        // https://mudblazor.com/features/icons#icons

        // https://github.com/serdarciplak/BlazorMonaco

        // See also https://codewithmukesh.com/blog/blazor-hero-quick-start-guide/#Dashboard
    }
}
