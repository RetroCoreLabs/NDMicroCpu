//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Core;
using NDMicroCpu.Machine;


TheMachine machine = new TheMachine(Enums.CPUType.ND110);

if (!machine.Initialize()) return;
//machine.InitializeRandomData();

Console.TreatControlCAsInput = true; // dont let ctrl-c kill the app, send it to server instead

//Utils.DisableLog = false;

machine.RunFromConsole();
