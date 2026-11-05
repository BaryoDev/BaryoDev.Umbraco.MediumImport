using Umbraco.Cms.Core.Packaging;

namespace BaryoDev.Umbraco.MediumImport.Identity;

public sealed class MediumImportMigrationPlan() : PackageMigrationPlan("BaryoDev.Umbraco.MediumImport")
{
    protected override void DefinePlan()
    {
        To<CreateTables>("6f1c3b7e-2a4d-4c59-9b0e-1d7a8e5c4f21");
    }
}
