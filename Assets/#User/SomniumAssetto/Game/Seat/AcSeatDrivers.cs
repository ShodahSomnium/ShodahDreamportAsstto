using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>[S] The seat's early frame (order -90, before the client's order-0 scripts read / place the body): riders, pending seat,
    /// input, keys, the early seat write. Added to the AcGame object by AcSeat.Start (never in the scene: nothing runs before the gate).</summary>
    [DefaultExecutionOrder(-90)]
    public sealed class AcSeatEarly : MonoBehaviour
    {
        private int _errors, _riderErrors;

        private void Update()
        {
            try { AcRiders.Early(); }
            catch (Exception e) { if (_riderErrors++ < 3) AcLog.Error("Seat: riders early pass failed: " + e); }
            try { AcSeat.Early(); }
            catch (Exception e) { if (_errors++ < 3) AcLog.Error("Seat: early frame failed: " + e); }
        }
    }

    /// <summary>[S] The seat's late frame (LateUpdate order 9000: after the client's camera brain at 100 and Fusion's Render, before VRIK at
    /// 9998): pose, seat, VR head, wheel grab, monitor camera, riders again.</summary>
    [DefaultExecutionOrder(9000)]
    public sealed class AcSeatLate : MonoBehaviour
    {
        private int _errors, _riderErrors;

        private void LateUpdate()
        {
            try { AcSeat.Late(); }
            catch (Exception e) { if (_errors++ < 3) AcLog.Error("Seat: late frame failed: " + e); }
            try { AcRiders.Late(); }
            catch (Exception e) { if (_riderErrors++ < 3) AcLog.Error("Seat: riders late pass failed: " + e); }
        }
    }

    /// <summary>[S] Seated avatars' hands on the wheel and the own head hide (LateUpdate 9997, after the client's Animator; Halo's order).</summary>
    [DefaultExecutionOrder(9997)]
    public sealed class AcSeatPosesDriver : MonoBehaviour
    {
        private int _errors;

        private void LateUpdate()
        {
            try { AcSeatPoses.Late(); }
            catch (Exception e) { if (_errors++ < 3) AcLog.Error("Seat: poses failed: " + e); }
        }
    }

    /// <summary>[S] Order 9990 (ETS EtsAvatarSync, AGENTS frame order): remembers the own monitor avatar after the client placed it (Update) and
    /// carries it onto this frame's seat after the seat's 9000 write (LateUpdate) - BEFORE the hands are IK'd onto the wheel at 9997, so the
    /// carry never moves posed hands off the rim.</summary>
    [DefaultExecutionOrder(9990)]
    public sealed class AcSeatCarry : MonoBehaviour
    {
        private int _errors;

        private void Update()
        {
            try { AcDeskCarry.Remember(); }
            catch (Exception e) { if (_errors++ < 3) AcLog.Error("Seat: carry (remember) failed: " + e); }
        }

        private void LateUpdate()
        {
            try { AcDeskCarry.Carry(); }
            catch (Exception e) { if (_errors++ < 3) AcLog.Error("Seat: carry failed: " + e); }
        }
    }

    /// <summary>[S] Order 10000: re-writes the seat if anything moved the body after order 9000 (Halo HaloVehicleLate); only then the monitor
    /// avatar is carried again (rare; logged once by AcSeat).</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class AcSeatFinal : MonoBehaviour
    {
        private int _errors;

        private void LateUpdate()
        {
            try { if (AcSeat.Final()) AcDeskCarry.Carry(); }
            catch (Exception e) { if (_errors++ < 3) AcLog.Error("Seat: final frame failed: " + e); }
        }
    }
}
