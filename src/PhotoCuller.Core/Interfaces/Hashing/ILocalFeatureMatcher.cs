namespace PhotoCuller.Core.Interfaces.Hashing;

/// <summary>
/// Verifies that two candidate photos share geometrically consistent local
/// image features and therefore depict the same scene.
/// </summary>
public interface ILocalFeatureMatcher
{
    Task<bool> IsSameSceneAsync(
        string firstFilePath,
        string secondFilePath,
        CancellationToken ct = default);
}
