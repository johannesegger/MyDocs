﻿using LexDbDal;
using Microsoft.Practices.ServiceLocation;
using MyDocs.Common.Model;
using MyDocs.WindowsStore.Service;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;

namespace MyDocs.WindowsStore
{
    public static class MigrationHelper
    {
        private static Version CurrentVersion
        {
            get
            {
                var v = Package.Current.Id.Version;
                return new Version(v.Major, v.Minor, v.Build, v.Revision);
            }
        }

        const string latestMigrationVersionKey = "migratedTo";
        private static Version MigrateFromVersion
        {
            get
            {
                var version = (string)ApplicationData.Current.LocalSettings.Values[latestMigrationVersionKey];
                return version != null ? Version.Parse(version) : null;
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values[latestMigrationVersionKey] = value.ToString();
            }
        }

        public static async Task Migrate()
        {
            if (MigrateFromVersion == null) {
                await MigrateFromBuiltInDbToLexDb();
            }
            MigrateFromVersion = CurrentVersion;
        }

        private static async Task MigrateFromBuiltInDbToLexDb()
        {
            var builtInDb = ServiceLocator.Current.GetInstance<ApplicationDataContainerDocumentStorage>();
            var documents = await builtInDb.GetAllDocumentsAsync();

            await ServiceLocator.Current.GetInstance<LexDocumentDb>().Setup(documents);
        }
    }
}
