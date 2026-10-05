namespace BoostParaPc.Services;

public static class SparklineMath
{
    /// <summary>Converte amostras em pontos de uma linha. A primeira amostra fica à esquerda, a última à direita; valores acima do máximo são cortados.</summary>
    public static IReadOnlyList<(double X, double Y)> ToPoints(IReadOnlyList<double> values, double width, double height, double max)
    {
        if (values.Count == 0 || width <= 0 || height <= 0) return [];
        if (max <= 0) max = 1;
        var step = values.Count == 1 ? 0 : width / (values.Count - 1);
        var points = new List<(double, double)>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var ratio = Math.Clamp(values[i] / max, 0, 1);
            points.Add((i * step, height - ratio * height));
        }
        return points;
    }
}
