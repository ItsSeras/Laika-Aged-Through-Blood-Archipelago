using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

public partial class ArchipelagoClientManager
{
    // Reflection helpers for Archipelago.MultiClient.Net compatibility.
    // These allow the client to tolerate packet/member shape differences between library versions.
    private object TryReadObjectProperty(object instance, params string[] propertyNames)
    {
        if (instance == null)
            return null;

        Type type = instance.GetType();

        foreach (string propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            if (property == null)
                continue;

            try
            {
                object value = property.GetValue(instance, null);
                if (value != null)
                    return value;
            }
            catch
            {
            }
        }

        return null;
    }

    private object TryReadObjectPropertyOrField(object instance, params string[] names)
    {
        if (instance == null)
            return null;

        Type type = instance.GetType();

        foreach (string name in names)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (property != null)
            {
                try
                {
                    object value = property.GetValue(instance, null);
                    if (value != null)
                        return value;
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field != null)
            {
                try
                {
                    object value = field.GetValue(instance);
                    if (value != null)
                        return value;
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private string TryReadStringProperty(object instance, params string[] propertyNames)
    {
        object value = TryReadObjectProperty(instance, propertyNames);

        if (value == null)
            return "";

        return value.ToString();
    }

    private long ReadLongProperty(object instance, params string[] propertyNames)
    {
        if (instance == null)
            return -1;

        Type type = instance.GetType();

        foreach (string propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            if (property == null)
                continue;

            object value = property.GetValue(instance, null);
            if (value == null)
                continue;

            try
            {
                return Convert.ToInt64(value);
            }
            catch
            {
            }
        }

        return -1;
    }

    private int ReadIntProperty(object instance, params string[] propertyNames)
    {
        if (instance == null)
            return 0;

        Type type = instance.GetType();

        foreach (string propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            if (property == null)
                continue;

            object value = property.GetValue(instance, null);
            if (value == null)
                continue;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
            }
        }

        return 0;
    }

    private string ReadStringProperty(object instance, params string[] propertyNames)
    {
        object value = ReadObjectProperty(instance, propertyNames);

        if (value == null)
            return string.Empty;

        return value.ToString();
    }

    private object ReadObjectProperty(object instance, params string[] propertyNames)
    {
        if (instance == null)
            return null;

        Type type = instance.GetType();

        foreach (string propertyName in propertyNames)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                continue;

            var property = type.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );

            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(instance, null);
                }
                catch
                {
                }
            }

            var field = type.GetField(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );

            if (field != null)
            {
                try
                {
                    return field.GetValue(instance);
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private object TryInvokeAny(object target, string[] methodNames, params object[] args)
    {
        if (target == null || methodNames == null)
            return null;

        Type type = target.GetType();

        foreach (string methodName in methodNames)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();

                if (parameters.Length != args.Length)
                    continue;

                try
                {
                    object[] convertedArgs = new object[args.Length];

                    for (int i = 0; i < args.Length; i++)
                        convertedArgs[i] = ConvertValueForMember(args[i], parameters[i].ParameterType);

                    object result = method.Invoke(target, convertedArgs);

                    if (result != null)
                        return result;
                }
                catch
                {
                    // Try another overload.
                }
            }
        }

        return null;
    }

    private void DumpObjectShape(string label, object target)
    {
        if (target == null)
        {
            LaikaMod.LogInfo($"{label}: <null>");
            return;
        }

        try
        {
            Type type = target.GetType();
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"{label}: type={type.FullName}");

            sb.AppendLine("Properties:");
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value = null;
                string valueText = "<unread>";

                try
                {
                    if (property.GetIndexParameters().Length == 0)
                    {
                        value = property.GetValue(target, null);
                        valueText = value == null ? "<null>" : value.ToString();
                    }
                }
                catch
                {
                }

                sb.AppendLine(
                    $"  {property.PropertyType.FullName} {property.Name} CanWrite={property.CanWrite} Value={valueText}"
                );
            }

            sb.AppendLine("Fields:");
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value = null;
                string valueText = "<unread>";

                try
                {
                    value = field.GetValue(target);
                    valueText = value == null ? "<null>" : value.ToString();
                }
                catch
                {
                }

                sb.AppendLine(
                    $"  {field.FieldType.FullName} {field.Name} Value={valueText}"
                );
            }

            LaikaMod.LogInfo(sb.ToString());
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"{label}: failed to dump object shape.\n{ex}");
        }
    }
}