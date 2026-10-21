using CommunityToolkit.Mvvm.ComponentModel;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Models;
using System.Collections.ObjectModel;

namespace PhotoCuller.App.WinUI.ViewModels;

/// <summary>View-model wrapper around a PhotoGroup for the similar-groups page.</summary>
public sealed partial class PhotoGroupViewModel : ObservableObject
{
    public PhotoGroup Model { get; }

    public PhotoGroupViewModel(PhotoGroup model, IEnumerable<PhotoItemViewModel> photos)
    {
        Model  = model;
        Photos = new ObservableCollection<PhotoItemViewModel>(photos);
    }

    public Guid     Id           => Model.Id;
    public GroupType GroupType   => Model.GroupType;
    public string   Name         => Model.Name;
    public int      PhotoCount   => Model.PhotoCount;
    public string   GroupTypeLabel => Model.GroupType switch
    {
        GroupType.ExactDuplicate => "重复",
        GroupType.Burst          => "连拍",
        GroupType.Similar        => "相似",
        _                        => "其他",
    };

    public string? RecommendedFileName
    {
        get
        {
            var rec = Photos.FirstOrDefault(p => p.Id == Model.RecommendedPhotoId);
            return rec?.FileName;
        }
    }

    public ObservableCollection<PhotoItemViewModel> Photos { get; }

    public PhotoItemViewModel? RecommendedPhoto =>
        Photos.FirstOrDefault(p => p.Id == Model.RecommendedPhotoId)
        ?? Photos.Where(p => p.Status != RecommendationStatus.ManualDelete && !p.IsMovedToTrash)
            .OrderByDescending(p => p.OverallScore ?? 0).FirstOrDefault();

    public void SetRecommendedPhoto(PhotoItemViewModel photo)
    {
        if (!Photos.Contains(photo))
            return;

        Model.RecommendedPhotoId = photo.Id;
        OnPropertyChanged(nameof(RecommendedFileName));
        OnPropertyChanged(nameof(RecommendedPhoto));
    }

#pragma warning disable MVVMTK0045
    [ObservableProperty] private bool _isExpanded = true;
#pragma warning restore MVVMTK0045
}
