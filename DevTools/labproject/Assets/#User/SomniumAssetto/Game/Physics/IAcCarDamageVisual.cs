namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [P, Physics] Iteration 2: the damage push target (DAMAGE_DESIGN 1.7 / 9 C). AcCarPhysics calls these on its Model when
    /// <see cref="AcCarDamage.Version"/> moved (local, AI and remote rigs alike). C's AcCarModel implements it with exactly these signatures
    /// (<c>public sealed class AcCarModel : IAcCarDamageVisual</c>): P cannot call methods that do not exist yet, so the push goes through
    /// this interface and lights up the moment C declares it - nothing else to wire.
    /// </summary>
    public interface IAcCarDamageVisual
    {
        /// <summary>Zone values in km/h as <see cref="AcCarDamage.ZoneByte"/> (whole km/h, 0..255) - identical on the owner and every remote
        /// client. Idempotent; all zero = pristine.</summary>
        void SetDamage(float frontKmh, float rearKmh, float leftKmh, float rightKmh, float centerKmh);

        /// <summary>Engine smoke 0 = off .. 1 (P sends 1 while <see cref="AcCarDamage.EngineSmoking"/>).</summary>
        void SetEngineSmoke(float rate01);
    }
}
