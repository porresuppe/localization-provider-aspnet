﻿// Copyright © 2017 Valdis Iljuconoks.
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Linq;
using DbLocalizationProvider.Abstractions;
using DbLocalizationProvider.Commands;

namespace DbLocalizationProvider.AspNet.Commands
{
    public class CreateNewResourceHandler : ICommandHandler<CreateNewResource.Command>
    {
        public void Execute(CreateNewResource.Command command)
        {
            if(string.IsNullOrEmpty(command.Key))
                throw new ArgumentNullException(nameof(command.Key));

            using(var db = new LanguageEntities(ConnectionStringHelper.ConnectionString))
            {
                var existingResource = db.LocalizationResources.FirstOrDefault(r => r.ResourceKey == command.Key);
                if(existingResource != null)
                {
                    throw new InvalidOperationException($"Resource with key `{command.Key}` already exists");
                }

                db.LocalizationResources.Add(new LocalizationResource(command.Key)
                                             {
                                                 ModificationDate = DateTime.UtcNow,
                                                 FromCode = command.FromCode,
                                                 IsModified = false,
                                                 Author = command.UserName
                                             });

                db.SaveChanges();
            }
        }
    }
}
