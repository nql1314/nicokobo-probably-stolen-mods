using Nicokobo.ContentInterop;

int checks = 0;
void Expect(bool condition, string message)
{ checks++; if (!condition) throw new InvalidOperationException(message); }

// Existing exhaustive story checks now link the migrated production rules and Newtonsoft codec.
StoryChecks.Run(Expect);

var gate = new OfficialStoryConversationSelection();
Expect(!gate.TryEnter(1, 2, true), "A stale run generation must not spend a fresh selection gate");
Expect(!gate.TryEnter(2, 2, false), "A callback outside its active visitor must not spend the gate");
Expect(gate.TryEnter(2, 2, true) && !gate.TryEnter(2, 2, true), "A live choice must enter exactly once");
var requests = new OfficialStoryConversationRequests();
Expect(requests.Reserve("nicokobo.aug", "one", "actor", "e07") == OfficialPresentationStatus.Queued, "First visit should reserve");
Expect(requests.Reserve("nicokobo.aug", "one", "actor", "e10") == OfficialPresentationStatus.Conflict, "A request cannot change its dialogue identity");
Expect(requests.Reserve("nicokobo.aug", "two", "actor", "e07") == OfficialPresentationStatus.Conflict, "One actor cannot occupy two live visits");
requests.Set("nicokobo.aug", "one", OfficialPresentationStatus.Dismissed);
Expect(requests.Reserve("nicokobo.aug", "two", "actor", "e07") == OfficialPresentationStatus.Queued, "A dismissed actor can return under a new request");

var automatic = new OfficialStoryConversationAutomaticSelection(.6);
var now = DateTime.UtcNow;
Expect(!automatic.TryEnter(true, now.AddSeconds(3)), "An automatic ending cannot fire before choices are displayed");
automatic.Displayed(now);
Expect(!automatic.TryEnter(true, now.AddSeconds(.5)), "Automatic choice must retain its visible delay");
Expect(!automatic.TryEnter(false, now.AddSeconds(1)), "A hidden choice must not fire");
Expect(!automatic.TryEnter(true, now.AddSeconds(2)), "Reopening a choice must restart the visible delay");
Expect(automatic.TryEnter(true, now.AddSeconds(2.7)) && !automatic.TryEnter(true, now.AddSeconds(5)), "The visible automatic ending must enter once");

foreach (int capacity in new[] { 3, 4 })
foreach (int count in new[] { 1, 3, 4, 6, 10, 17 })
{
    var root = new Dialogue().SetText("speaker", "body");
    var offered = new List<Dialogue>();
    for (int i = 0; i < count; i++)
    {
        var choice = OfficialStoryNativeChoice.Add(root, new(), "offer" + i, new Dialogue(), null);
        offered.Add(choice);
    }
    OfficialStoryNativeChoice.Paginate(root, capacity, "next", "previous", body => new Dialogue().SetText("speaker", body),
        (page, label, next) => OfficialStoryNativeChoice.Add(page, new(), label, next, null));
    var seen = new HashSet<Dialogue>(); var discovered = new List<Dialogue>(); var queue = new Queue<Dialogue>(); queue.Enqueue(root);
    while (queue.Count > 0)
    {
        var page = queue.Dequeue(); if (!seen.Add(page)) continue;
        Expect(page.choices.Count <= capacity, "Pagination must fit the real native button capacity");
        foreach (var choice in page.choices)
        {
            if (offered.Contains(choice.Item2!)) discovered.Add(choice.Item2!);
            else if (choice.Item2?.nextDialogue != null) queue.Enqueue(choice.Item2.nextDialogue);
        }
    }
    Expect(discovered.Count == count && discovered.Distinct().Count() == count && offered.All(discovered.Contains),
        "Every quoted option must remain reachable exactly once through native pagination");
}

var input = new WindowInputCapture();
input.Open(10, true);
Expect(input.BlocksPointer(10, true, true), "Opening by a click must capture its held gesture");
input.Close(11, true);
Expect(input.BlocksPointer(12, false, true) && input.BlocksPointer(13, false, false), "Closing must capture the closing release");
Expect(!input.BlocksPointer(14, false, false), "The next idle frame must release input capture");
input.Open(20, false);
Expect(!input.BlocksPointer(21, false, true) && !input.BlocksPointer(22, true, true) && !input.BlocksPointer(23, true, false),
    "A drag originating outside must receive its own release even when ending over a story window");
Expect(input.BlocksPointer(24, true, false), "The story window must capture the next hover frame");

Console.WriteLine($"Official story checks passed: {checks}. Production V4 rules/codec, dialogue identity, once-only choices, three/four-button pagination, automatic visibility and pointer release.");
