using System.Collections.Generic;

namespace RegionsAndSocieties
{
    public class SimplePriorityQueue<T>
    {
        private readonly List<KeyValuePair<T, float>> elements = new List<KeyValuePair<T, float>>();

        public int Count => elements.Count;

        public void Enqueue(T item, float priority)
        {
            elements.Add(new KeyValuePair<T, float>(item, priority));
        }

        public T Dequeue()
        {
            int minIndex = 0;
            for (int i = 1; i < elements.Count; i++)
            {
                if (elements[i].Value < elements[minIndex].Value)
                {
                    minIndex = i;
                }
            }
            T item = elements[minIndex].Key;
            elements.RemoveAt(minIndex);
            return item;
        }
    }
}
