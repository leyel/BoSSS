set output 'MPIScalingTimes.tex'
set terminal cairolatex  pdf   size 17cm,17cm
set multiplot
set size 1,0.5
set size 0.98,0.49
set origin 0.01,0.505
unset lmargin 
unset rmargin 
set tmargin 4e00
set bmargin 1e00
set logscale x 2
unset logscale y
unset logscale x2
unset logscale y2
set xrange [2:64]
set autoscale y
set autoscale x2
set autoscale y2
unset xlabel
set ylabel "Time [s]"
unset x2label
unset y2label
unset title 
unset key
set key outside right vertical maxrows 1 
set xtics format "$2^{%L}$" 
set x2tics format " " 
set ytics 
set y2tics format " " 
set termoption dashed
plot "MPIScalingTimes_data_0.csv" title "NewtonGmres Swz w Coarse Overlap MGLevels2" with linespoints linecolor  "black" dashtype 3 linewidth 3 pointtype 4 pointsize 0.5
set size 0.98,0.49
set origin 0.01,0.005
unset lmargin 
unset rmargin 
set tmargin 1e00
unset bmargin 
set logscale x 2
set logscale y 2
unset logscale x2
unset logscale y2
set xrange [2:64]
set autoscale y
set autoscale x2
set autoscale y2
set xlabel "Processors"
set ylabel "Speedup"
unset x2label
unset y2label
unset title 
unset key
set key outside right vertical maxrows 2 
set xtics format "$2^{%L}$" 
set x2tics format " " 
set ytics format "$2^{%L}$" 
set y2tics format " " 
set termoption dashed
plot "MPIScalingTimes_data_1.csv" title "NewtonGmres Swz w Coarse Overlap MGLevels2" with linespoints linecolor  "black" dashtype 3 linewidth 3 pointtype 4 pointsize 0.5, "MPIScalingTimes_data_2.csv" title "Ideal" with lines linecolor  "black" dashtype 1 linewidth 3


exit
