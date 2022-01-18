public class Promise
{
    public static Promise Defer()
    {
        var p = new Promise();
        p.IsExecuting = true;
        return p;
    }

    private bool IsExecuting { get; set; }
    private Promise Next { get; set; }
    public bool IsResolved { get; set; }
    public string Tag { get; set; }

    public Promise Then(Func<Promise, Promise> deferred)
    {
        if (Next == null)
        {
            Next = new Promise();
        }

        if (Next.IsResolved) return Next;

        if (IsResolved && !Next.IsResolved || IsExecuting)
        {
            Next.IsExecuting = true;
            var result = deferred(Next);
            Next.IsExecuting = false;
            return result;
        }

        return this;
    }

    public Promise Then(Action deferred)
    {
        return Then(x =>
        {
            deferred();
            return x.Resolve();
        });
    }

    public Promise Resolve()
    {
        var skipOne = false;
        IsResolved = true;
        return Then(x =>
        {
            if (!skipOne)
            {
                skipOne = true;
                return x;
            }

            x.IsResolved = true;
            return x;
        });
    }

    public Promise Repeat()
    {
        Reset(this);
        return this;
    }

    private void Reset(Promise p)
    {
        if (p.Next != null)
        {
            p.Next.IsResolved = false;
            Reset(p.Next);
        }
    }
}