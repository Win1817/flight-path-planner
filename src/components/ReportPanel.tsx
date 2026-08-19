import { useState } from 'react';
import { FileText, Shield, Download, Check, ChevronsUpDown } from 'lucide-react';
import { cn } from '@/lib/utils';
import type { ParsedAor, ParsedOps } from '@/types/ops';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Button } from '@/components/ui/button';
import { MatchedOpsList } from '@/components/MatchedOpsList';

interface ReportPanelProps {
  aors: ParsedAor[];
  hasOps: boolean;
  selectedAorId: string | null;
  onSelectAor: (id: string) => void;
  matchingOps: ParsedOps[];
  activeOpId: string | null;
  hoveredOpId: string | null;
  onActivateOp: (id: string) => void;
  onHoverOp: (id: string | null) => void;
  onExport: (format: 'json' | 'xlsx') => void;
}

export function ReportPanel({
  aors,
  hasOps,
  selectedAorId,
  onSelectAor,
  matchingOps,
  activeOpId,
  hoveredOpId,
  onActivateOp,
  onHoverOp,
  onExport,
}: ReportPanelProps) {
  const [pickerOpen, setPickerOpen] = useState(false);

  if (aors.length === 0 || !hasOps) {
    return (
      <div className="flex flex-col items-center justify-center py-12 text-center">
        <div className="p-4 rounded-full bg-muted/50 mb-4">
          <FileText className="w-8 h-8 text-muted-foreground" />
        </div>
        <p className="text-sm text-muted-foreground">
          Upload both OPS and AoR data to generate a report
        </p>
      </div>
    );
  }

  const selectedAor = aors.find(a => a.id === selectedAorId);

  return (
    <div className="space-y-4">
      <div className="space-y-2 pb-4 border-b border-sidebar-border">
        <span className="text-xs font-medium text-muted-foreground">
          Geozone Report ({aors.length} AoR{aors.length !== 1 ? 's' : ''})
        </span>
        <p className="text-xs text-muted-foreground">
          Exports match counts for every AoR against the current OPS timeframe/filters.
        </p>
        <div className="grid grid-cols-2 gap-2">
          <Button onClick={() => onExport('json')} className="w-full gap-2" variant="outline" size="sm">
            <Download className="w-3.5 h-3.5" />
            Export JSON
          </Button>
          <Button onClick={() => onExport('xlsx')} className="w-full gap-2" variant="outline" size="sm">
            <Download className="w-3.5 h-3.5" />
            Export XLSX
          </Button>
        </div>
      </div>

      <div className="space-y-2">
        <span className="text-xs font-medium text-muted-foreground">Area of Responsibility</span>
        <Popover open={pickerOpen} onOpenChange={setPickerOpen}>
          <PopoverTrigger asChild>
            <Button
              variant="outline"
              role="combobox"
              aria-expanded={pickerOpen}
              className="w-full justify-between font-normal"
            >
              <span className="truncate">
                {selectedAor ? `${selectedAor.name} (${selectedAor.designator})` : 'Select an AoR...'}
              </span>
              <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
            </Button>
          </PopoverTrigger>
          <PopoverContent className="w-[--radix-popover-trigger-width] p-0">
            <Command>
              <CommandInput placeholder="Search AoRs..." className="h-9" />
              <CommandList>
                <CommandEmpty>No AoR found.</CommandEmpty>
                <CommandGroup>
                  {aors.map(aor => (
                    <CommandItem
                      key={aor.id}
                      value={`${aor.name} ${aor.designator} ${aor.id}`}
                      onSelect={() => { onSelectAor(aor.id); setPickerOpen(false); }}
                    >
                      <Check className={cn('mr-2 h-4 w-4', selectedAorId === aor.id ? 'opacity-100' : 'opacity-0')} />
                      <span className="truncate">{aor.name} ({aor.designator})</span>
                    </CommandItem>
                  ))}
                </CommandGroup>
              </CommandList>
            </Command>
          </PopoverContent>
        </Popover>
      </div>

      {!selectedAor && (
        <p className="text-xs text-muted-foreground text-center py-6">
          Select an AoR above, or click one on the map, to see the flight plans inside it.
        </p>
      )}

      {selectedAor && (
        <>
          <div className="glass-panel rounded-lg p-4 space-y-1">
            <div className="flex items-center gap-2">
              <Shield className="w-4 h-4 text-primary" />
              <h3 className="text-sm font-semibold text-foreground truncate">{selectedAor.name}</h3>
            </div>
            <p className="text-2xl font-bold text-foreground">{matchingOps.length}</p>
            <p className="text-xs text-muted-foreground">
              flight plan{matchingOps.length !== 1 ? 's' : ''} in this area within the selected timeframe
            </p>
          </div>

          <MatchedOpsList
            ops={matchingOps}
            activeOpId={activeOpId}
            hoveredOpId={hoveredOpId}
            onActivateOp={onActivateOp}
            onHoverOp={onHoverOp}
            emptyMessage="No flight plans intersect this area in the selected timeframe."
          />
        </>
      )}
    </div>
  );
}
