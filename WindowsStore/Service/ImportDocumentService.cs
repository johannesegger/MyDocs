﻿using MyDocs.Common;
using MyDocs.Common.Contract.Service;
using Logic = MyDocs.Common.Model.Logic;
using Serializable = MyDocs.Common.Model.Serializable;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.IO;
using MyDocs.Common.Model.Logic;

namespace MyDocs.WindowsStore.Service
{
    public class ImportDocumentService : IImportDocumentService
    {
        private readonly IFileOpenPickerService fileOpenPickerService;
        private readonly IDocumentService documentService;
        private readonly IPageExtractor pageExtractor;
        private readonly ISettingsService settingsService;

        public ImportDocumentService(IFileOpenPickerService fileOpenPickerService, IDocumentService documentService, IPageExtractor pageExtractor, ISettingsService settingsService)
        {
            this.fileOpenPickerService = fileOpenPickerService;
            this.documentService = documentService;
            this.pageExtractor = pageExtractor;
            this.settingsService = settingsService;
        }

        public async Task ImportDocuments()
        {
            var zipFile = await fileOpenPickerService.PickOpenFileAsync(new List<string> { ".zip" });
            if (zipFile == null) {
                return;
            }
            using (var zipFileStream = (await zipFile.OpenReadAsync()).AsStream())
            using (var archive = new ZipArchive(zipFileStream, ZipArchiveMode.Read)) {
                var metaInfoEntry = archive.GetEntry("Documents.xml");
                if (metaInfoEntry == null) {
                    throw new ImportManifestNotFoundException();
                }
                using (var metaInfoStream = metaInfoEntry.Open()) {
                    var serializer = new DataContractSerializer(typeof(IEnumerable<Serializable.Document>)/*, "Documents", "http://mydocs.eggapauli"*/);
                    var serializedDocuments = (IEnumerable<Serializable.Document>)serializer.ReadObject(metaInfoStream);
                    foreach (var serializedDocument in serializedDocuments) {
                        var document = await DeserializeDocumentAsync(archive, serializedDocument);
                        await documentService.SaveDocumentAsync(document);
                    }
                }
            }
        }

        private async Task<Logic.Document> DeserializeDocumentAsync(ZipArchive archive, Serializable.Document document)
        {
            // TODO it smells that we create subdocuments based on a document and then return a different document
            var doc = new Logic.Document(document.Id, document.Category, document.DateAdded, document.Lifespan, document.HasLimitedLifespan, document.Tags);
            var subDocuments = new List<SubDocument>();
            foreach (var fileName in document.Files) {
                subDocuments.Add(await DeserializePhotosAsync(archive, doc, fileName));
            }
            // TODO use https://github.com/AArnott/ImmutableObjectGraph ?
            return new Document(doc.Id, doc.Category, doc.DateAdded, doc.Lifespan, doc.HasLimitedLifespan, doc.Tags, subDocuments);
        }

        private async Task<Logic.SubDocument> DeserializePhotosAsync(ZipArchive archive, Logic.Document document, string fileName)
        {
            // Folders must be separated by "/", not by "\\"
            //var path = Path.Combine(document.GetHumanReadableDescription(), fileName);
            var path = document.GetHumanReadableDescription() + "/" + fileName;
            var dirEntry = archive.GetEntry(document.GetHumanReadableDescription());
            var entry = archive.GetEntry(path);
            if (entry == null) {
                // TODO refine
                throw new Exception("Entry no found.");
            }
            // TODO inject more specific service for extracting files
            var photoFile = await settingsService.PhotoFolder.CreateFileAsync(fileName);
            using (var entryStream = entry.Open())
            using (var photoWriter = await photoFile.OpenStreamForWriteAsync()) {
                await entryStream.CopyToAsync(photoWriter);
            }

            // TODO strip name collision part out
            var pages =
                pageExtractor.SupportsExtension(Path.GetExtension(fileName)) ?
                await pageExtractor.ExtractPages(photoFile, document) :
                null;
            return new Logic.SubDocument(photoFile, pages);
        }
    }
}
