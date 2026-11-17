using System;
using System.Globalization;
using SodaTest;

internal static class Exact
{
    public const double Gamma = 1.4;
    public const double DensityLeft = 1.0;
    public const double PressureLeft = 1.0;
    public const double DensityRight = 0.125;
    public const double PressureRight = 0.1;
    public const double Membrane = 0.5;

    public static double StarPressure()
    {
        double p = 0.3;
        for (int n = 0; n < 200; n++)
        {
            double f = FL(p) + FR(p);
            double df = (FL(p + 1e-8) + FR(p + 1e-8) - f) / 1e-8;
            double next = p - f / df;
            if (!(next > 1e-6)) next = p * 0.5;
            if (Math.Abs(next - p) < 1e-13) { p = next; break; }
            p = next;
        }
        return p;
    }

    private static double FL(double p)
    {
        if (p > PressureLeft)
            return (p - PressureLeft) * Math.Sqrt(2.0 / ((Gamma + 1.0) * DensityLeft) /
                (p + (Gamma - 1.0) / (Gamma + 1.0) * PressureLeft));
        double c = Math.Sqrt(Gamma * PressureLeft / DensityLeft);
        return 2.0 * c / (Gamma - 1.0) * (Math.Pow(p / PressureLeft, (Gamma - 1.0) / (2.0 * Gamma)) - 1.0);
    }

    private static double FR(double p)
    {
        if (p > PressureRight)
            return (p - PressureRight) * Math.Sqrt(2.0 / ((Gamma + 1.0) * DensityRight) /
                (p + (Gamma - 1.0) / (Gamma + 1.0) * PressureRight));
        double c = Math.Sqrt(Gamma * PressureRight / DensityRight);
        return 2.0 * c / (Gamma - 1.0) * (Math.Pow(p / PressureRight, (Gamma - 1.0) / (2.0 * Gamma)) - 1.0);
    }

    public static double StarVelocity()
    {
        double pStar = StarPressure();
        return 0.5 * (FR(pStar) - FL(pStar));
    }

    public static double ShockPosition(double t)
    {
        double pStar = StarPressure();
        double cRight = Math.Sqrt(Gamma * PressureRight / DensityRight);
        double coefficient = Math.Sqrt(1.0 + (Gamma + 1.0) / (2.0 * Gamma) * (pStar / PressureRight - 1.0));
        return Membrane + cRight * coefficient * t;
    }

    public static void At(double x, double t, out double rho, out double p, out double u)
    {
        double pStar = StarPressure();
        double cLeft = Math.Sqrt(Gamma * PressureLeft / DensityLeft);
        double cRight = Math.Sqrt(Gamma * PressureRight / DensityRight);
        double uStar = 0.5 * (FR(pStar) - FL(pStar));
        double cStar = cLeft * Math.Pow(pStar / PressureLeft, (Gamma - 1.0) / (2.0 * Gamma));

        double head = Membrane - cLeft * t;
        double tail = Membrane + (uStar - cStar) * t;
        double contact = Membrane + uStar * t;
        double shock = Membrane + uStar * Math.Sqrt((Gamma + 1.0) / (2.0 * Gamma) * pStar / PressureRight
                                                     + (Gamma - 1.0) / (2.0 * Gamma)) * t;

        rho = DensityLeft; p = PressureLeft; u = 0.0;

        if (t <= 0.0 || x <= head) return;

        if (x <= tail)
        {
            double xi = (x - Membrane) / t;
            double c = (2.0 * cLeft - (Gamma - 1.0) * xi) / (Gamma + 1.0);
            u = 2.0 * (cLeft + xi) / (Gamma + 1.0);
            rho = DensityLeft * Math.Pow(c / cLeft, 2.0 / (Gamma - 1.0));
            p = PressureLeft * Math.Pow(c / cLeft, 2.0 * Gamma / (Gamma - 1.0));
            return;
        }

        if (x <= contact)
        {
            rho = DensityLeft * Math.Pow(pStar / PressureLeft, 1.0 / Gamma);
            p = pStar; u = uStar;
            return;
        }

        if (x <= shock)
        {
            double ratio = pStar / PressureRight;
            rho = DensityRight * ((Gamma + 1.0) * ratio + (Gamma - 1.0)) /
                                 ((Gamma - 1.0) * ratio + (Gamma + 1.0));
            p = pStar; u = uStar;
            return;
        }

        rho = DensityRight; p = PressureRight; u = 0.0;
    }
}
