interface Props {
  draft: string;
  onApply: (text: string) => void;
  onRefresh: () => void;
}

export default function SuggestedReply({ draft, onApply, onRefresh }: Props) {
  return (
    <div className="border border-blue-200 bg-blue-50 rounded-lg p-3 mx-4 mb-2">
      <div className="flex items-center justify-between mb-1">
        <span className="text-xs font-medium text-blue-600">AI Suggestion</span>
        <button onClick={onRefresh} className="text-blue-400 hover:text-blue-600 text-xs">Refresh</button>
      </div>
      <p className="text-sm text-slate-700 mb-2">{draft}</p>
      <button onClick={() => onApply(draft)}
        className="text-xs bg-blue-500 text-white px-3 py-1 rounded hover:bg-blue-600">Apply</button>
    </div>
  );
}
