import React from 'react';
import { Composition } from 'remotion';
import { DURATION, TimeVideo } from './TimeVideo';

export const Root: React.FC = () => (
  <Composition id="TimeVideo" component={TimeVideo} durationInFrames={DURATION} fps={30} width={1920} height={1080} />
);
