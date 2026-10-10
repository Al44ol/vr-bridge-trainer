// ============================================================
// NelderMead.cs — Симплекс-метод Нелдера–Мида (A3.2, идентификация параметров)
//
// Nelder J.A., Mead R. A simplex method for function minimization.
// The Computer Journal, 1965, 7(4), 308–313.
// Коэффициенты: отражение 1, растяжение 2, сжатие 0,5, редукция 0,5.
// Детерминирован (без случайных чисел); после сходимости — один перезапуск
// из найденной точки (Press et al., Numerical Recipes, §10.5), чтобы не
// остановиться на вырожденном симплексе.
// ============================================================
using System;

public sealed class NelderMeadResult
{
    public double[] X;
    public double F;
    public int Evaluations;
    public bool Converged;
}

public static class NelderMead
{
    /// <param name="f">Минимизируемая функция.</param>
    /// <param name="x0">Начальная точка.</param>
    /// <param name="step">Начальный размер симплекса по каждой координате.</param>
    /// <param name="maxEvaluations">Предел числа вычислений f.</param>
    /// <param name="tolF">Сходимость: разброс f в симплексе (относительный + абсолютный 1e-12).</param>
    public static NelderMeadResult Minimize(Func<double[], double> f, double[] x0, double[] step,
                                            int maxEvaluations = 2000, double tolF = 1e-10)
    {
        var first = Run(f, x0, step, maxEvaluations, tolF);
        var second = Run(f, first.X, step, Math.Max(10, maxEvaluations - first.Evaluations), tolF);
        second.Evaluations += first.Evaluations;
        return second.F <= first.F ? second : first;
    }

    private static NelderMeadResult Run(Func<double[], double> f, double[] x0, double[] step, int maxEval, double tolF)
    {
        int n = x0.Length;
        var p = new double[n + 1][];
        var y = new double[n + 1];
        int evals = 0;
        double Eval(double[] x) { evals++; double v = f(x); return double.IsNaN(v) ? double.MaxValue : v; }

        for (int i = 0; i <= n; i++)
        {
            p[i] = (double[])x0.Clone();
            if (i > 0) p[i][i - 1] += step[i - 1];
            y[i] = Eval(p[i]);
        }

        var centroid = new double[n];
        var xr = new double[n]; var xe = new double[n]; var xc = new double[n];
        bool converged = false;
        while (evals < maxEval)
        {
            // Сортировка вершин по f (n мало — вставками)
            for (int i = 1; i <= n; i++)
                for (int j = i; j > 0 && y[j] < y[j - 1]; j--)
                {
                    (y[j], y[j - 1]) = (y[j - 1], y[j]);
                    (p[j], p[j - 1]) = (p[j - 1], p[j]);
                }

            if (Math.Abs(y[n] - y[0]) <= tolF * (Math.Abs(y[0]) + Math.Abs(y[n])) + 1e-12) { converged = true; break; }

            for (int k = 0; k < n; k++)
            {
                double s = 0;
                for (int i = 0; i < n; i++) s += p[i][k];
                centroid[k] = s / n;
            }

            for (int k = 0; k < n; k++) xr[k] = centroid[k] + (centroid[k] - p[n][k]);
            double yr = Eval(xr);
            if (yr < y[0])
            {
                for (int k = 0; k < n; k++) xe[k] = centroid[k] + 2.0 * (centroid[k] - p[n][k]);
                double ye = Eval(xe);
                if (ye < yr) { p[n] = (double[])xe.Clone(); y[n] = ye; }
                else { p[n] = (double[])xr.Clone(); y[n] = yr; }
                continue;
            }
            if (yr < y[n - 1]) { p[n] = (double[])xr.Clone(); y[n] = yr; continue; }

            // Сжатие (внешнее, если отражённая лучше худшей, иначе внутреннее)
            bool outside = yr < y[n];
            for (int k = 0; k < n; k++)
                xc[k] = outside ? centroid[k] + 0.5 * (xr[k] - centroid[k]) : centroid[k] + 0.5 * (p[n][k] - centroid[k]);
            double yc = Eval(xc);
            if (yc < (outside ? yr : y[n])) { p[n] = (double[])xc.Clone(); y[n] = yc; continue; }

            // Редукция к лучшей вершине
            for (int i = 1; i <= n; i++)
            {
                for (int k = 0; k < n; k++) p[i][k] = p[0][k] + 0.5 * (p[i][k] - p[0][k]);
                y[i] = Eval(p[i]);
            }
        }

        int best = 0;
        for (int i = 1; i <= n; i++) if (y[i] < y[best]) best = i;
        return new NelderMeadResult { X = (double[])p[best].Clone(), F = y[best], Evaluations = evals, Converged = converged };
    }
}
