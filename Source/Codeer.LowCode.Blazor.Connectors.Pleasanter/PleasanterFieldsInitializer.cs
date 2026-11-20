namespace Codeer.LowCode.Blazor.Connectors.Pleasanter
{
    /// <summary>
    /// このパッケージのフィールドを使えるようにする。本体はデザインのフィールドの型を読み込み済みのアセンブリから探すので、
    /// デザインを読む前にこのアセンブリを読み込んでおく。クライアント (Blazor) とサーバーの起動時に 1 回呼ぶ (デザイナは .Designer の Initialize が呼ぶ)。
    /// </summary>
    public static class PleasanterFieldsInitializer
    {
        public static void Initialize() => typeof(PleasanterAttachmentsFieldDesign).ToString();
    }
}
