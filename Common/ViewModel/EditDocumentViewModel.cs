﻿using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using MyDocs.Common.Contract.Page;
using MyDocs.Common.Contract.Service;
using MyDocs.Common.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MyDocs.Common.ViewModel
{
    public class EditDocumentViewModel : ViewModelBase
    {
        private readonly IDocumentService documentService;
        private readonly INavigationService navigator;
        private readonly IUserInterfaceService uiService;
        private readonly ICameraService cameraService;
        private readonly IFileOpenPickerService filePicker;
        private readonly ISettingsService settingsService;
        private readonly IPageExtractor pageExtractor;

        #region Properties

        private bool showNewCategoryInput;
        private string useCategoryName;
        private string newCategoryName;
        private Document originalDocument;
        private Document editingDocument;
        private Photo selectedPhoto;
        private bool isBusy;

        public IEnumerable<string> CategoryNames
        {
            get { return documentService.GetCategoryNames(); }
        }

        public bool ShowNewCategoryInput
        {
            get { return !HasCategories || showNewCategoryInput; }
            set
            {
                if (Set(ref showNewCategoryInput, value)) {
                    RaisePropertyChanged(() => ShowUseCategoryInput);
                    SaveDocumentCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool ShowUseCategoryInput
        {
            get { return !ShowNewCategoryInput; }
            set { ShowNewCategoryInput = !value; }
        }

        public bool HasCategories
        {
            get { return CategoryNames.Any(); }
        }

        public string UseCategoryName
        {
            get { return useCategoryName; }
            set
            {
                if (Set(ref useCategoryName, value)) {
                    SaveDocumentCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string NewCategoryName
        {
            get { return newCategoryName; }
            set
            {
                if (Set(ref newCategoryName, value)) {
                    SaveDocumentCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public Document EditingDocument
        {
            get { return editingDocument; }
            set
            {
                if (editingDocument != null) {
                    editingDocument.PropertyChanged -= EditingDocumentChangedHandler;
                }
                if (editingDocument != value) {
                    if (value != null) {
                        editingDocument = value.Clone();
                        NewCategoryName = editingDocument.Category;
                        UseCategoryName = editingDocument.Category;
                        editingDocument.PropertyChanged += EditingDocumentChangedHandler;
                        editingDocument.Photos.CollectionChanged += EditingDocumentPhotoCollectionChanged;
                    }
                    else {
                        editingDocument = null;
                    }
                    originalDocument = value;
                    RaisePropertyChanged();
                    SaveDocumentCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public Guid EditingDocumentId
        {
            set
            {
                EditingDocument = null;
                documentService.GetDocumentById(value).ContinueWith(t =>
                {
                    if (t.IsFaulted) {
                        EditingDocument = new Document();
                        uiService.ShowErrorAsync("documentNotFound");
                    }
                    else {
                        EditingDocument = t.Result;
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        private void EditingDocumentChangedHandler(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Tags" || e.PropertyName == "Category") {
                SaveDocumentCommand.RaiseCanExecuteChanged();
            }
        }

        private void EditingDocumentPhotoCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            SaveDocumentCommand.RaiseCanExecuteChanged();
        }

        public Photo SelectedPhoto
        {
            get { return selectedPhoto; }
            set
            {
                if (Set(ref selectedPhoto, value)) {
                    RemovePhotoCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsBusy
        {
            get { return isBusy; }
            set { Set(ref isBusy, value); }
        }

        #endregion

        public EditDocumentViewModel(
            IDocumentService documentService,
            INavigationService navigator,
            IUserInterfaceService uiService,
            ICameraService cameraService,
            IFileOpenPickerService filePicker,
            ISettingsService settingsService,
            IPageExtractor pageExtractor)
        {
            this.documentService = documentService;
            this.navigator = navigator;
            this.uiService = uiService;
            this.cameraService = cameraService;
            this.filePicker = filePicker;
            this.settingsService = settingsService;
            this.pageExtractor = pageExtractor;

            CreateCommands();
            CreateDesignTimeData();

            ShowUseCategoryInput = documentService.GetCategoryNames().Any();
        }

        [Conditional("DEBUG")]
        private void CreateDesignTimeData()
        {
            if (IsInDesignMode) {
                documentService.LoadAsync().ContinueWith(t =>
                {
                    EditingDocument = t.Result.SelectMany(c => c.Documents).First();
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        public async Task LoadAsync()
        {
            using (SetBusy()) {
                await documentService.LoadAsync();
            }
        }

        #region Commands

        public RelayCommand ShowNewCategoryCommand { get; private set; }
        public RelayCommand ShowUseCategoryCommand { get; private set; }
        public RelayCommand AddPhotoFromCameraCommand { get; private set; }
        public RelayCommand AddPhotoFromFileCommand { get; private set; }
        public RelayCommand RemovePhotoCommand { get; private set; }
        public RelayCommand SaveDocumentCommand { get; private set; }

        private void CreateCommands()
        {
            AddPhotoFromCameraCommand = new RelayCommand(AddPhotoFromCameraAsync);
            AddPhotoFromFileCommand = new RelayCommand(AddPhotoFromFileAsync);
            RemovePhotoCommand = new RelayCommand(RemovePhoto, () => SelectedPhoto != null);
            SaveDocumentCommand = new RelayCommand(SaveDocumentAsync, () =>
                EditingDocument != null
                && EditingDocument.Tags.Any()
                && (ShowNewCategoryInput && !String.IsNullOrWhiteSpace(NewCategoryName)
                    || ShowUseCategoryInput && !String.IsNullOrWhiteSpace(UseCategoryName))
                && EditingDocument.Photos.Count > 0);
            ShowNewCategoryCommand = new RelayCommand(() => { ShowNewCategoryInput = true; });
            ShowUseCategoryCommand = new RelayCommand(() => { ShowNewCategoryInput = false; });
        }

        private async void SaveDocumentAsync()
        {
            EditingDocument.Category = ShowNewCategoryInput ? NewCategoryName : UseCategoryName;

            using (SetBusy()) {
                await documentService.SaveDocumentAsync(EditingDocument);

                // Delete removed photos
                if (originalDocument != null) {
                    var deletedPhotos = originalDocument.Photos.Where(p => !EditingDocument.Photos.Contains(p));
                    await documentService.RemovePhotosAsync(deletedPhotos);
                }
            }

            var doc = EditingDocument;
            EditingDocument = null;
            originalDocument = doc;

            navigator.Navigate<IMainPage>(originalDocument.Id);
        }

        private async void AddPhotoFromCameraAsync()
        {
            var photo = await cameraService.CapturePhotoAsync();
            if (photo != null) {
                EditingDocument.Photos.Add(photo);
            }
        }

        private async void AddPhotoFromFileAsync()
        {
            var files = await filePicker.PickFilesForDocumentAsync(EditingDocument);

            using (SetBusy()) {
                var error = false;
                foreach (var file in files) {
                    try {
                        var pages =
                            pageExtractor.SupportedExtensions.Contains(Path.GetExtension(file.Name)) ?
                            await pageExtractor.ExtractPages(file, EditingDocument) :
                            null;
                        EditingDocument.Photos.Add(new Photo(file.DisplayName, file, pages));
                    }
                    catch (Exception) {
                        error = true;
                    }
                }

                if (error) {
                    await uiService.ShowErrorAsync("fileLoadError");
                }
            }
        }

        private void RemovePhoto()
        {
            EditingDocument.Photos.Remove(SelectedPhoto);
        }

        #endregion

        private IDisposable SetBusy()
        {
            return new TemporaryState(() => IsBusy = true, () => IsBusy = false);
        }
    }
}
