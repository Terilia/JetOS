using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        static class MapContactStoreV2
        {
            static readonly List<MapContactV2> _contacts = new List<MapContactV2>();

            public static void Update(char kind, long id, Vector3D position, Vector3D velocity, string name, long observerId, int hopCount, double ageSeconds)
            {
                if (id == 0 || !RadarContactV2.IsMapKind(kind))
                    return;

                var contact = new MapContactV2(kind, id, position, velocity, name, observerId, hopCount);
                if (ageSeconds > 0)
                    contact.LastSeen = Jet.GameSeconds - ageSeconds;
                for (int i = 0; i < _contacts.Count; i++)
                {
                    if (_contacts[i].Id == id)
                    {
                        if (contact.LastSeen < _contacts[i].LastSeen)
                            return;
                        if (SE(contact.Name))
                            contact.Name = _contacts[i].Name;
                        _contacts[i] = contact;
                        return;
                    }
                }

                _contacts.Add(contact);
            }

            public static List<MapContactV2> GetActive()
            {
                Decay();
                return _contacts;
            }

            public static void Decay()
            {
                for (int i = _contacts.Count - 1; i >= 0; i--)
                    if (_contacts[i].AgeSeconds > RadarContactV2.CONTACT_DECAY_SECONDS)
                        _contacts.RemoveAt(i);
            }
        }
    }
}
