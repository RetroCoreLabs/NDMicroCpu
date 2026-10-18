//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NDMicroCpu.Core;
using NDMicroCpu.Web.Objects;
using static System.Net.WebRequestMethods;

namespace NDMicroCpu.Web.Services
{
    /// <summary>
    /// The application service the web host registers; adds nothing to <see cref="IAppService"/> today.
    /// </summary>
    public class AppService : IAppService
    {

        /// <summary>
        /// Constructor 
        /// </summary>
        /// <param name="hostEnvironment"></param>
        public AppService(IWebAssemblyHostEnvironment hostEnvironment)
        {
            _baseAddress = hostEnvironment.BaseAddress;
            Utils.OnLogMsg += Utils_OnLogMsg;
        }

        private void Utils_OnLogMsg(string msg, bool eol)
        {
            LogMsg(msg, eol);
        }
    }
}
