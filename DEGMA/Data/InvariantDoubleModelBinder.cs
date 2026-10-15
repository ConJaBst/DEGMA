using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Globalization;

namespace DEGMA.Binders
{
    public class InvariantDoubleModelBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var valueResult = bindingContext.ValueProvider
                .GetValue(bindingContext.ModelName);

            if (valueResult == ValueProviderResult.None)
                return Task.CompletedTask;

            bindingContext.ModelState.SetModelValue(
                bindingContext.ModelName, valueResult);

            var value = valueResult.FirstValue;

            if (string.IsNullOrEmpty(value))
                return Task.CompletedTask;

            if (double.TryParse(value, NumberStyles.Any,
                CultureInfo.InvariantCulture, out var result))
            {
                bindingContext.Result = ModelBindingResult.Success(result);
            }
            else
            {
                bindingContext.ModelState.TryAddModelError(
                    bindingContext.ModelName,
                    $"The value '{value}' is not a valid number.");
            }

            return Task.CompletedTask;
        }
    }

    public class InvariantDoubleModelBinderProvider : IModelBinderProvider
    {
        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            if (context.Metadata.ModelType == typeof(double) ||
                context.Metadata.ModelType == typeof(double?))
                return new InvariantDoubleModelBinder();

            return null;
        }
    }
}