using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public class PIDController
        {
            public double Kp { get; set; } // Proportional gain
            public double Ki { get; set; } // Integral gain
            public double Kd { get; set; } // Derivative gain

            private double integral;
            private double previousError;
            private double outputMin;
            private double outputMax;

            public PIDController(
                double kp,
                double ki,
                double kd,
                double outputMin = double.MinValue,
                double outputMax = double.MaxValue
            )
            {
                Kp = kp;
                Ki = ki;
                Kd = kd;
                this.outputMin = outputMin;
                this.outputMax = outputMax;
                integral = 0;
                previousError = 0;
            }

            public double Update(double setpoint, double pv, double deltaTime)
            {
                double error = setpoint - pv;

                // Integral term with anti-windup
                integral += error * deltaTime;
                integral = MathHelper.Clamp(integral, -100, 100); // Adjust limits as needed

                // Derivative term
                double derivative = (error - previousError) / deltaTime;

                // PID output
                double output = (Kp * error) + (Ki * integral) + (Kd * derivative);

                // Clamp output
                output = MathHelper.Clamp(output, outputMin, outputMax);

                previousError = error;

                return output;
            }

            public void Reset()
            {
                integral = 0;
                previousError = 0;
            }
        }
    }
}
