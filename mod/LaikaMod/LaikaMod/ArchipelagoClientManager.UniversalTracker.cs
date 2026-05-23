using Archipelago.MultiClient.Net;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

public partial class ArchipelagoClientManager
{
    // Universal Tracker data storage support.
    // Sends the player's current major region to AP data storage so UT can auto-tab maps.
    public bool SendUniversalTrackerRegion(string regionName, int mapIndex)
    {
        if (session == null)
        {
            LaikaMod.LogWarning($"UT MAP: cannot send current region={regionName}; AP session is null.");
            return false;
        }

        if (LaikaMod.SessionState == null ||
            LaikaMod.SessionState.Connection == null ||
            !LaikaMod.SessionState.Connection.IsAuthenticated)
        {
            LaikaMod.LogWarning($"UT MAP: cannot send current region={regionName}; AP connection is not authenticated.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(regionName))
            return false;

        try
        {
            int protocolTeam = LaikaMod.SessionState.Connection.Team; // Usually 0 for Team #1
            int displayTeam = protocolTeam + 1;                       // Usually 1 for Team #1
            int slot = LaikaMod.SessionState.Connection.Slot;

            JObject mapPayload = new JObject
            {
                ["index"] = mapIndex,
                ["region"] = regionName,
                ["nonce"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            string protocolKey = $"laika_current_region_{protocolTeam}_{slot}";
            string displayKey = $"laika_current_region_{displayTeam}_{slot}";

            bool protocolSent = SendDataStorageReplaceValue(protocolKey, mapPayload);
            bool displaySent = true;

            if (!string.Equals(protocolKey, displayKey, StringComparison.OrdinalIgnoreCase))
                displaySent = SendDataStorageReplaceValue(displayKey, mapPayload);

            if (!protocolSent && !displaySent)
            {
                LaikaMod.LogWarning(
                    $"UT MAP: data storage Set packet was not sent. keys={protocolKey}, {displayKey}, value={mapPayload.ToString(Newtonsoft.Json.Formatting.None)}"
                );

                return false;
            }

            LaikaMod.LogInfo(
                $"UT MAP: sent data storage Set packet keys {protocolKey} and {displayKey} = " +
                $"{mapPayload.ToString(Newtonsoft.Json.Formatting.None)} " +
                $"(AP protocol team={protocolTeam}, display team={displayTeam}, slot={slot})"
            );

            return true;
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"UT MAP: failed to send current region={regionName}\n{ex}");
            return false;
        }
    }

    private bool SendDataStorageReplaceValue(string key, object value)
    {
        // Archipelago.MultiClient.Net has changed API shapes across versions.
        // This reflection path avoids hard-binding us to one exact helper API.
        Type setPacketType = Type.GetType(
            "Archipelago.MultiClient.Net.Packets.SetPacket, Archipelago.MultiClient.Net"
        );

        if (setPacketType == null)
        {
            LaikaMod.LogWarning("UT MAP: SetPacket type was not found in Archipelago.MultiClient.Net.");
            return false;
        }

        object packet = Activator.CreateInstance(setPacketType);

        TrySetPacketMember(packet, "Key", key);
        TrySetPacketMember(packet, "key", key);

        JObject defaultPayload = new JObject
        {
            ["index"] = 0,
            ["region"] = "Start / Tutorial Area",
            ["nonce"] = 0
        };

        TrySetPacketMember(packet, "DefaultValue", defaultPayload);
        TrySetPacketMember(packet, "Default", defaultPayload);
        TrySetPacketMember(packet, "default", defaultPayload);

        TrySetPacketMember(packet, "WantReply", true);
        TrySetPacketMember(packet, "want_reply", true);

        object operation = CreateReplaceOperation(value, setPacketType);

        if (operation == null)
        {
            LaikaMod.LogWarning("UT MAP: failed to create SetPacket replace operation.");
            return false;
        }

        bool operationsSet = SetOperationsMember(packet, operation);

        if (!operationsSet)
        {
            LaikaMod.LogWarning("UT MAP: failed to attach SetPacket operations.");
            return false;
        }

        // DumpObjectShape("UT MAP DEBUG: SetPacket after setup", packet);
        // DumpObjectShape("UT MAP DEBUG: SetPacket operation after setup", operation);

        ArchipelagoPacketBase typedPacket = packet as ArchipelagoPacketBase;

        if (typedPacket == null)
        {
            LaikaMod.LogWarning("UT MAP: SetPacket was not an ArchipelagoPacketBase.");
            return false;
        }

        session.Socket.SendPacket(typedPacket);
        return true;
    }

    private object CreateReplaceOperation(object value, Type setPacketType)
    {
        Type operationType = GetSetPacketOperationElementType(setPacketType);

        if (operationType == null)
        {
            LaikaMod.LogWarning("UT MAP: could not determine SetPacket operation element type.");
            return null;
        }

        object operation = TryCreateReplaceOperationByConstructor(operationType, value);

        if (operation != null)
        {
            LaikaMod.LogInfo($"UT MAP DEBUG: created replace operation by constructor. Type={operationType.FullName}");
            return operation;
        }

        operation = TryCreateReplaceOperationByMembers(operationType, value);

        if (operation != null)
        {
            //LaikaMod.LogInfo($"UT MAP DEBUG: created replace operation by member assignment. Type={operationType.FullName}");
            return operation;
        }

        DumpOperationTypeShape(operationType);

        LaikaMod.LogWarning(
            $"UT MAP: failed to create replace operation. OperationType={operationType.FullName}"
        );

        return null;
    }

    private Type GetSetPacketOperationElementType(Type setPacketType)
    {
        if (setPacketType == null)
            return null;

        var operationsMember =
            setPacketType.GetProperty("Operations") ??
            setPacketType.GetProperty("operations");

        if (operationsMember != null)
        {
            Type operationsType = operationsMember.PropertyType;

            if (operationsType.IsArray)
                return operationsType.GetElementType();

            if (operationsType.IsGenericType)
            {
                Type[] args = operationsType.GetGenericArguments();

                if (args.Length == 1)
                    return args[0];
            }
        }

        var operationsField =
            setPacketType.GetField("Operations") ??
            setPacketType.GetField("operations");

        if (operationsField != null)
        {
            Type operationsType = operationsField.FieldType;

            if (operationsType.IsArray)
                return operationsType.GetElementType();

            if (operationsType.IsGenericType)
            {
                Type[] args = operationsType.GetGenericArguments();

                if (args.Length == 1)
                    return args[0];
            }
        }

        return Type.GetType(
            "Archipelago.MultiClient.Net.Models.OperationSpecification, Archipelago.MultiClient.Net"
        );
    }

    private object TryCreateReplaceOperationByConstructor(Type operationType, object value)
    {
        if (operationType == null)
            return null;

        foreach (ConstructorInfo constructor in operationType.GetConstructors())
        {
            ParameterInfo[] parameters = constructor.GetParameters();

            if (parameters.Length != 2)
                continue;

            try
            {
                object firstArg = BuildReplaceOperationValue(parameters[0].ParameterType);
                object secondArg = ConvertValueForMember(value, parameters[1].ParameterType);

                if (firstArg == null)
                    continue;

                object operation = constructor.Invoke(new object[] { firstArg, secondArg });

                LaikaMod.LogInfo(
                    $"UT MAP DEBUG: OperationSpecification constructor matched: " +
                    $"{parameters[0].ParameterType.FullName}, {parameters[1].ParameterType.FullName}"
                );

                return operation;
            }
            catch
            {
                // Try next constructor shape.
            }

            try
            {
                object firstArg = ConvertValueForMember(value, parameters[0].ParameterType);
                object secondArg = BuildReplaceOperationValue(parameters[1].ParameterType);

                if (secondArg == null)
                    continue;

                object operation = constructor.Invoke(new object[] { firstArg, secondArg });

                LaikaMod.LogInfo(
                    $"UT MAP DEBUG: OperationSpecification constructor matched reversed: " +
                    $"{parameters[0].ParameterType.FullName}, {parameters[1].ParameterType.FullName}"
                );

                return operation;
            }
            catch
            {
                // Try next constructor shape.
            }
        }

        return null;
    }

    private object TryCreateReplaceOperationByMembers(Type operationType, object value)
    {
        if (operationType == null)
            return null;

        object operation = Activator.CreateInstance(operationType);

        bool operationSet =
            TrySetOperationMember(operation, "Operation", "replace") ||
            TrySetOperationMember(operation, "operation", "replace") ||
            TrySetOperationMember(operation, "OperationType", "replace") ||
            TrySetOperationMember(operation, "operationType", "replace") ||
            TrySetOperationMember(operation, "Type", "replace") ||
            TrySetOperationMember(operation, "type", "replace");

        if (!operationSet)
            return null;

        bool valueSet =
            TrySetPacketMember(operation, "Value", value) ||
            TrySetPacketMember(operation, "value", value);

        if (!valueSet)
            return null;

        return operation;
    }

    private bool TrySetOperationMember(object target, string name, string operationName)
    {
        if (target == null || string.IsNullOrWhiteSpace(name))
            return false;

        Type type = target.GetType();

        var property = type.GetProperty(name);
        if (property != null && property.CanWrite)
        {
            object converted = BuildReplaceOperationValue(property.PropertyType);

            if (converted == null)
                return false;

            property.SetValue(target, converted, null);
            return true;
        }

        var field = type.GetField(name);
        if (field != null)
        {
            object converted = BuildReplaceOperationValue(field.FieldType);

            if (converted == null)
                return false;

            field.SetValue(target, converted);
            return true;
        }

        return false;
    }

    private bool TrySetDataStorageOperationReplace(object operation)
    {
        if (operation == null)
            return false;

        Type operationObjectType = operation.GetType();

        var operationProperty =
            operationObjectType.GetProperty("Operation") ??
            operationObjectType.GetProperty("operation");

        if (operationProperty != null && operationProperty.CanWrite)
        {
            object replaceValue = BuildReplaceOperationValue(operationProperty.PropertyType);

            if (replaceValue != null)
            {
                operationProperty.SetValue(operation, replaceValue, null);
                LaikaMod.LogInfo(
                    $"UT MAP DEBUG: set operation property {operationProperty.Name} " +
                    $"to {replaceValue} ({operationProperty.PropertyType.FullName})"
                );
                return true;
            }
        }

        var operationField =
            operationObjectType.GetField("Operation") ??
            operationObjectType.GetField("operation");

        if (operationField != null)
        {
            object replaceValue = BuildReplaceOperationValue(operationField.FieldType);

            if (replaceValue != null)
            {
                operationField.SetValue(operation, replaceValue);
                LaikaMod.LogInfo(
                    $"UT MAP DEBUG: set operation field {operationField.Name} " +
                    $"to {replaceValue} ({operationField.FieldType.FullName})"
                );
                return true;
            }
        }

        return false;
    }

    private object BuildReplaceOperationValue(Type targetType)
    {
        if (targetType == null)
            return null;

        if (targetType == typeof(string))
            return "replace";

        if (typeof(Newtonsoft.Json.Linq.JToken).IsAssignableFrom(targetType))
            return Newtonsoft.Json.Linq.JToken.FromObject("replace");

        if (targetType.IsEnum)
        {
            foreach (string enumName in Enum.GetNames(targetType))
            {
                if (string.Equals(enumName, "Replace", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(enumName, "DataStorageOperationType.Replace", StringComparison.OrdinalIgnoreCase))
                {
                    return Enum.Parse(targetType, enumName);
                }
            }

            foreach (string enumName in Enum.GetNames(targetType))
            {
                if (enumName.IndexOf("Replace", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    enumName.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return Enum.Parse(targetType, enumName);
                }
            }

            LaikaMod.LogWarning(
                "UT MAP: could not find Replace enum value. Available operation enum values: " +
                string.Join(", ", Enum.GetNames(targetType))
            );

            return null;
        }

        return null;
    }

    private void DumpOperationTypeShape(Type operationType)
    {
        if (operationType == null)
            return;

        try
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"UT MAP DEBUG: operation type shape for {operationType.FullName}");

            sb.AppendLine("Constructors:");
            foreach (ConstructorInfo constructor in operationType.GetConstructors())
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                List<string> parts = new List<string>();

                foreach (ParameterInfo parameter in parameters)
                {
                    parts.Add($"{parameter.ParameterType.FullName} {parameter.Name}");
                }

                sb.AppendLine("  .ctor(" + string.Join(", ", parts.ToArray()) + ")");
            }

            sb.AppendLine("Properties:");
            foreach (PropertyInfo property in operationType.GetProperties())
            {
                sb.AppendLine(
                    $"  {property.PropertyType.FullName} {property.Name} CanWrite={property.CanWrite}"
                );
            }

            sb.AppendLine("Fields:");
            foreach (FieldInfo field in operationType.GetFields())
            {
                sb.AppendLine($"  {field.FieldType.FullName} {field.Name}");
            }

            LaikaMod.LogInfo(sb.ToString());
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"UT MAP DEBUG: failed to dump operation type shape.\n{ex}");
        }
    }

    private bool SetOperationsMember(object packet, object operation)
    {
        if (packet == null || operation == null)
            return false;

        Type packetType = packet.GetType();

        var operationsProperty =
            packetType.GetProperty("Operations") ??
            packetType.GetProperty("operations");

        if (operationsProperty != null && operationsProperty.CanWrite)
        {
            Type operationsType = operationsProperty.PropertyType;

            if (operationsType.IsArray)
            {
                Array array = Array.CreateInstance(operation.GetType(), 1);
                array.SetValue(operation, 0);
                operationsProperty.SetValue(packet, array, null);
                return true;
            }

            if (operationsType.IsGenericType)
            {
                object list = Activator.CreateInstance(operationsType);
                var addMethod = operationsType.GetMethod("Add");

                if (addMethod != null)
                {
                    addMethod.Invoke(list, new object[] { operation });
                    operationsProperty.SetValue(packet, list, null);
                    return true;
                }
            }
        }

        var operationsField =
            packetType.GetField("Operations") ??
            packetType.GetField("operations");

        if (operationsField != null)
        {
            Type operationsType = operationsField.FieldType;

            if (operationsType.IsArray)
            {
                Array array = Array.CreateInstance(operation.GetType(), 1);
                array.SetValue(operation, 0);
                operationsField.SetValue(packet, array);
                return true;
            }

            if (operationsType.IsGenericType)
            {
                object list = Activator.CreateInstance(operationsType);
                var addMethod = operationsType.GetMethod("Add");

                if (addMethod != null)
                {
                    addMethod.Invoke(list, new object[] { operation });
                    operationsField.SetValue(packet, list);
                    return true;
                }
            }
        }

        LaikaMod.LogWarning(
            $"UT MAP: failed to attach operation to SetPacket. PacketType={packetType.FullName}, OperationType={operation.GetType().FullName}"
        );

        return false;
    }

    private bool TrySetPacketMember(object target, string name, object value)
    {
        if (target == null || string.IsNullOrWhiteSpace(name))
            return false;

        Type type = target.GetType();

        var property = type.GetProperty(name);
        if (property != null && property.CanWrite)
        {
            object convertedValue = ConvertValueForMember(value, property.PropertyType);
            property.SetValue(target, convertedValue, null);
            return true;
        }

        var field = type.GetField(name);
        if (field != null)
        {
            object convertedValue = ConvertValueForMember(value, field.FieldType);
            field.SetValue(target, convertedValue);
            return true;
        }

        return false;
    }

    private object ConvertValueForMember(object value, Type targetType)
    {
        if (targetType == null)
            return value;

        if (value == null)
            return null;

        if (targetType.IsInstanceOfType(value))
            return value;

        // Archipelago.MultiClient.Net's SetPacket operations use JToken for JSON values.
        if (typeof(Newtonsoft.Json.Linq.JToken).IsAssignableFrom(targetType))
        {
            return Newtonsoft.Json.Linq.JToken.FromObject(value);
        }

        // Nullable<T> support, just in case one packet shape uses nullable fields.
        Type nullableType = Nullable.GetUnderlyingType(targetType);
        if (nullableType != null)
        {
            return Convert.ChangeType(value, nullableType);
        }

        // Basic primitive conversion fallback.
        if (targetType.IsEnum)
        {
            return Enum.Parse(targetType, value.ToString());
        }

        return Convert.ChangeType(value, targetType);
    }
}