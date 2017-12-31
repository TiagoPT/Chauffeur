﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Chauffeur.Host;
using Umbraco.Core.Models;
using Umbraco.Core.Services;
using Task = System.Threading.Tasks.Task;

namespace Chauffeur.Deliverables
{
    [DeliverableName("package")]
    [DeliverableAlias("p")]
    [DeliverableAlias("pkg")]
    public sealed class PackageDeliverable : Deliverable
    {
        private readonly IFileSystem fileSystem;
        private readonly IChauffeurSettings settings;
        private readonly IPackagingService packagingService;
        private readonly IContentTypeService contentTypeService;

        public PackageDeliverable(
            TextReader reader,
            TextWriter writer,
            IFileSystem fileSystem,
            IChauffeurSettings settings,
            IPackagingService packagingService,
            IContentTypeService contentTypeService)
            : base(reader, writer)
        {
            this.fileSystem = fileSystem;
            this.settings = settings;
            this.packagingService = packagingService;
            this.contentTypeService = contentTypeService;
        }

        public override async Task<DeliverableResponse> Run(string command, string[] args)
        {
            var packages = args.Where(a => !a.StartsWith("-f:"));

            if (!packages.Any())
            {
                await Out.WriteLineAsync("No packages were provided, use `help package` to see usage");
                return DeliverableResponse.Continue;
            }

            string chauffeurFolder;

            var overridePath = args.FirstOrDefault(a => a.StartsWith("-f:"));
            if (overridePath != null)
            {
                chauffeurFolder = overridePath.Replace("-f:", string.Empty);
            }
            else
            {
                if (!settings.TryGetChauffeurDirectory(out chauffeurFolder))
                    return DeliverableResponse.Continue;
            }

            var tasks = packages.Select(pkg => Unpack(pkg, chauffeurFolder));
            await Task.WhenAll(tasks);

            return DeliverableResponse.Continue;
        }

        private async Task Unpack(string name, string chauffeurFolder)
        {
            var fileLocation = fileSystem.Path.Combine(chauffeurFolder, name + ".xml");
            if (!fileSystem.File.Exists(fileLocation))
            {
                await Out.WriteLineFormattedAsync("The package '{0}' is not found in the Chauffeur folder", name);
                return;
            }

            using (var stream = fileSystem.File.OpenRead(fileLocation))
            {
                var xml = XDocument.Load(stream);

                var info = xml.Root.Element("info");

                if (info != null)
                    await PrintInfo(info);

                var element = xml.Root.Element("DataTypes");
                if (element != null)
                    await UnpackDataTypes(element.Elements("DataType"));

                element = xml.Root.Element("Templates");
                if (element != null)
                    await UnpackTemplates(element.Elements("Template"));

                element = xml.Root.Element("Macros");
                if (element != null)
                    await UnpackMacros(element.Elements("macro"));

                element = xml.Root.Element("DocumentTypes");
                if (element != null)
                {
                    await UnpackDocumentTypes(element);
                }
                else if (xml.Root.Name == "DocumentType")
                {
                    await UnpackDocumentTypes(xml.Root);
                }
            }
        }

        private async Task PrintInfo(XElement info)
        {
            var pkg = info.Element("package");

            if (pkg == null)
                return;

            var name = (string)pkg.Element("name");
            var version = (string)pkg.Element("version");

            await Out.WriteLineFormattedAsync("Installing package {0} v{1}", name, version);
        }

        private async Task UnpackDataTypes(IEnumerable<XElement> elements)
        {
            foreach (var element in elements)
            {
                var name = (string)element.Attribute("Name");
                await Out.WriteLineFormattedAsync("Importing DataType '{0}'", name);
                packagingService.ImportDataTypeDefinitions(new XElement("DataTypes", element));
            }
        }

        private async Task UnpackTemplates(IEnumerable<XElement> elements)
        {
            foreach (var element in elements)
            {
                var name = (string)element.Element("Name");
                await Out.WriteLineFormattedAsync("Importing Template '{0}'", name);
                packagingService.ImportTemplates(element);
            }
        }

        private async Task UnpackMacros(IEnumerable<XElement> elements)
        {
            foreach (var element in elements)
            {
                var name = (string)element.Element("name");
                await Out.WriteLineFormattedAsync("Importing Macro '{0}'", name);
                packagingService.ImportMacros(element);
            }
        }

        private async Task<IEnumerable<IContentType>> UnpackDocumentTypes(XElement elements)
        {
            await Out.WriteLineAsync("Importing content types");
            return packagingService.ImportContentTypes(elements);
        }
    }
}
