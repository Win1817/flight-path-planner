import { Link } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { Plane, Shield } from 'lucide-react';
import { LandingMap } from '@/components/LandingMap';

const Landing = () => {
  return (
    <div className="relative flex flex-col items-center justify-center min-h-screen bg-background overflow-hidden">
      <LandingMap />
      <div className="relative z-10 flex flex-col items-center justify-center text-center p-8">
        <div className="p-4 rounded-full bg-primary/10 w-fit mx-auto mb-4 backdrop-blur-sm">
          <Plane className="w-10 h-10 text-primary" />
        </div>
        <h1 className="text-5xl font-bold text-foreground mb-4 drop-shadow-lg">UAS Tool</h1>
        <p className="text-lg text-muted-foreground mb-8 max-w-lg drop-shadow-sm">
          Visualize and manage your UAS OPS and Areas of Responsibility.
        </p>
        <div className="flex gap-4">
          <Link to="/app?tab=ops">
            <Button size="lg" className="gap-2 backdrop-blur-sm">
              <Plane className="w-5 h-5" />
              OPS
            </Button>
          </Link>
          <Link to="/app?tab=aors">
            <Button size="lg" variant="outline" className="gap-2 backdrop-blur-sm">
              <Shield className="w-5 h-5" />
              AoRs
            </Button>
          </Link>
        </div>
      </div>
    </div>
  );
};

export default Landing;
