namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    sealed class ModelPicker : SearchDropDown<ModelChoice>
    {
        public ModelPicker() : base(nameof(ModelChoice.Model), choice => choice.Model) { RefreshLanguage(); }
        public void RefreshLanguage() => ConfigureLanguage(
            UiText.Resolve("Rechercher un modèle…", "Search models…"),
            UiText.Resolve("Activez des modèles pour ce fournisseur dans Réglages > Fournisseurs.", "Enable models for this provider in Settings > Providers."),
            UiText.Resolve("Aucun modèle correspondant pour ce fournisseur.", "No matching model for this provider."),
            UiText.Resolve("Afficher les modèles du fournisseur", "Show provider models"));
    }
    sealed record ProviderChoice(int Id, string Name)
    {
        public string Caption => Name + " #" + Id;
    }
    sealed class ProviderPicker : SearchDropDown<ProviderChoice>
    {
        public ProviderPicker() : base(nameof(ProviderChoice.Caption), choice => choice.Caption) { RefreshLanguage(); }
        public void RefreshLanguage() => ConfigureLanguage(
            UiText.Resolve("Rechercher un fournisseur…", "Search providers…"),
            UiText.Resolve("Ajoutez un fournisseur dans Réglages > Fournisseurs.", "Add a provider in Settings > Providers."),
            UiText.Resolve("Aucun fournisseur correspondant.", "No matching provider."),
            UiText.Resolve("Afficher les fournisseurs", "Show providers"));
    }
}
