import { useCallback, useState } from 'react';
import { Upload, FileJson, AlertCircle, CheckCircle2 } from 'lucide-react';
import { cn } from '@/lib/utils';

interface FileUploadProps {
  onFileLoad: (data: unknown, fileName: string) => void;
  onError: (error: string) => void;
  label: string;
}

export function FileUpload({ onFileLoad, onError, label }: FileUploadProps) {
  const [isDragging, setIsDragging] = useState(false);
  const [fileName, setFileName] = useState<string | null>(null);
  const [status, setStatus] = useState<'idle' | 'success' | 'error'>('idle');

  const processFile = useCallback((file: File) => {
    if (!file.name.endsWith('.json')) {
      setStatus('error');
      onError('Please upload a JSON file');
      return;
    }

    const reader = new FileReader();
    reader.onload = (e) => {
      try {
        const data = JSON.parse(e.target?.result as string);
        setFileName(file.name);
        setStatus('success');
        onFileLoad(data, file.name);
      } catch {
        setStatus('error');
        onError('Invalid JSON format');
      }
    };
    reader.onerror = () => {
      setStatus('error');
      onError('Failed to read file');
    };
    reader.readAsText(file);
  }, [onFileLoad, onError]);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
    
    const file = e.dataTransfer.files[0];
    if (file) {
      processFile(file);
    }
  }, [processFile]);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
  }, []);

  const handleFileSelect = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      processFile(file);
    }
  }, [processFile]);

  return (
    <div
      onDrop={handleDrop}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      className={cn(
        'relative border-2 border-dashed rounded-lg p-6 transition-all duration-200 cursor-pointer',
        'hover:border-primary/50 hover:bg-primary/5',
        isDragging && 'border-primary bg-primary/10 scale-[1.02]',
        status === 'success' && 'border-emerald-500/50 bg-emerald-500/5',
        status === 'error' && 'border-destructive/50 bg-destructive/5',
        !isDragging && status === 'idle' && 'border-border'
      )}
    >
      <input
        type="file"
        accept=".json"
        onChange={handleFileSelect}
        className="absolute inset-0 w-full h-full opacity-0 cursor-pointer"
      />
      
      <div className="flex flex-col items-center gap-3 text-center">
        {status === 'success' ? (
          <div className="p-3 rounded-full bg-emerald-500/20">
            <CheckCircle2 className="w-6 h-6 text-emerald-400" />
          </div>
        ) : status === 'error' ? (
          <div className="p-3 rounded-full bg-destructive/20">
            <AlertCircle className="w-6 h-6 text-destructive" />
          </div>
        ) : isDragging ? (
          <div className="p-3 rounded-full bg-primary/20">
            <FileJson className="w-6 h-6 text-primary" />
          </div>
        ) : (
          <div className="p-3 rounded-full bg-muted">
            <Upload className="w-6 h-6 text-muted-foreground" />
          </div>
        )}

        {fileName ? (
          <div>
            <p className="text-sm font-medium text-foreground">{fileName}</p>
            <p className="text-xs text-muted-foreground mt-1">Click to upload a different file</p>
          </div>
        ) : (
          <div>
            <p className="text-sm font-medium text-foreground">
              {isDragging ? 'Drop your file here' : label}
            </p>
            <p className="text-xs text-muted-foreground mt-1">
              Drag & drop or click to browse
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
