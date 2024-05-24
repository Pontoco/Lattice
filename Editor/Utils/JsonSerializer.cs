using System;
using Lattice.Base;
using UnityEditor;

// This does not work at runtime because it uses the editor json library.
// Todo(john): Remove/simplify. This is primarily used for copy/paste serialization to text.
// Use new json library instead? (note: needs to support unity objects)

namespace Lattice.Editor.Utils
{
    [Serializable]
    public struct JsonElement
    {
        public string type;
        public string jsonDatas;

        public override string ToString()
        {
            return "type: " + type + " | JSON: " + jsonDatas;
        }
    }

    public static class JsonSerializer
    {
        public static JsonElement Serialize(object obj)
        {
            JsonElement elem = new JsonElement();

            elem.type = obj.GetType().AssemblyQualifiedName;
            elem.jsonDatas = EditorJsonUtility.ToJson(obj);

            return elem;
        }

        public static T Deserialize<T>(JsonElement e)
        {
            if (typeof(T) != Type.GetType(e.type))
            {
                throw new ArgumentException("Deserializing type is not the same than Json element type");
            }

            var obj = Activator.CreateInstance<T>();
            EditorJsonUtility.FromJsonOverwrite(e.jsonDatas, obj);

            return obj;
        }

        public static JsonElement SerializeNode(BaseNode node)
        {
            return Serialize(node);
        }

        public static BaseNode DeserializeNode(JsonElement e)
        {
            try
            {
                var baseNodeType = Type.GetType(e.type);

                if (e.jsonDatas == null)
                {
                    return null;
                }

                var node = Activator.CreateInstance(baseNodeType) as BaseNode;

                EditorJsonUtility.FromJsonOverwrite(e.jsonDatas, node);

                return node;
            }
            catch
            {
                return null;
            }
        }
    }
}
