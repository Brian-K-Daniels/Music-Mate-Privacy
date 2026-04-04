namespace musicmate.Services
{
    public partial class OrientationService : IOrientationService
    {
        public void ForceLandscape()
        {
            SetLandscapePlatform();
        }

        public void AllowAutorotate()
        {
            AllowAutorotatePlatform();
        }

        partial void SetLandscapePlatform();
        partial void AllowAutorotatePlatform();
    }
}
