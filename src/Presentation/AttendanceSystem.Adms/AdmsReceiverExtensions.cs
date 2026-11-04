using Microsoft.Extensions.DependencyInjection;

namespace AttendanceSystem.Adms;

public static class AdmsReceiverExtensions
{
    /// <summary>Agrega los endpoints ADMS (<see cref="AdmsController"/>) a la aplicación MVC del host.</summary>
    public static IMvcBuilder AddAdmsReceiver(this IMvcBuilder mvc) =>
        mvc.AddApplicationPart(typeof(AdmsController).Assembly);
}
